using NUnit.Framework;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace viewer.Tests {

    /// <summary>
    /// Precaricamento delle pagine successive.
    /// Prima: una pagina alla volta (3 pagine nuove = 12,7 s misurati contro 2,8 s in parallelo, 19/10/2026)
    /// e un solo precaricamento per volta: se ne girava uno (es. del registro appena lasciato), quello
    /// del registro nuovo non partiva affatto.
    /// </summary>
    [TestFixture, CancelAfter(15000)]
    public class PrecacherTests {

        /// <summary>download finto: ogni codice finisce quando il test lo decide (o subito)</summary>
        sealed class FakeLoads {
            public readonly ConcurrentDictionary<string, TaskCompletionSource<string>> Pending = new ConcurrentDictionary<string, TaskCompletionSource<string>>();
            public readonly ConcurrentQueue<string> Started = new ConcurrentQueue<string>();
            int active, maxActive;
            public int MaxActive => Volatile.Read(ref maxActive);
            public volatile bool Instant;
            public async Task<string> Load(string code) {
                Started.Enqueue(code);
                int n = Interlocked.Increment(ref active);
                int m; while (n > (m = Volatile.Read(ref maxActive)) && Interlocked.CompareExchange(ref maxActive, n, m) != m) { }
                try {
                    if (Instant) return code + ".jpg";
                    return await Pending.GetOrAdd(code, _ => new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
                }
                finally { Interlocked.Decrement(ref active); }
            }
            public void Finish(string code, string result = "ok") =>
                Pending.GetOrAdd(code, _ => new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult(result);
            /// <summary>finisce quelli in corso e fa finire subito anche quelli che partiranno dopo</summary>
            public void FinishAll() { Instant = true; foreach (var kv in Pending) kv.Value.TrySetResult("ok"); }
        }

        static void WaitUntil(Func<bool> cond, int ms = 3000) {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!cond()) {
                if (sw.ElapsedMilliseconds > ms) Assert.Fail("condizione non raggiunta entro " + ms + " ms");
                Thread.Sleep(10);
            }
        }

        static string[] Codes(string prefix, int n) => Enumerable.Range(1, n).Select(i => prefix + i).ToArray();

        [Test]
        public void TrePagineInsieme_NellOrdineDiLettura() {
            var f = new FakeLoads();
            var p = new Precacher(maxParallel: 3);
            p.Start(Codes("p", 10), f.Load);
            WaitUntil(() => f.Started.Count == 3);
            Thread.Sleep(100);
            Assert.That(f.Started.Count, Is.EqualTo(3), "non piu' di 3 insieme");
            // le prime tre pagine (partono in tre thread: fra loro l'ordine puo' variare di qualche ms)
            Assert.That(f.Started.ToArray(), Is.EquivalentTo(new[] { "p1", "p2", "p3" }));

            f.Finish("p1");
            WaitUntil(() => f.Started.Count == 4);
            Assert.That(f.Started.Last(), Is.EqualTo("p4"), "appena si libera un posto parte la pagina successiva");
            f.FinishAll();
            WaitUntil(() => f.Started.Count == 10 && !p.IsRunning);
            Assert.That(f.MaxActive, Is.EqualTo(3));
        }

        [Test]
        public void NuovoPrecaricamento_ParteSubito_IlVecchioSiFerma() {
            // Era il caso del doppio click su un altro registro: il nuovo precaricamento non partiva
            var f = new FakeLoads();
            var p = new Precacher(maxParallel: 3);
            p.Start(Codes("vecchio", 10), f.Load);
            WaitUntil(() => f.Started.Count == 3);

            p.Start(Codes("nuovo", 10), f.Load);
            WaitUntil(() => f.Started.Count(c => c.StartsWith("nuovo")) >= 1, 500);

            f.FinishAll();
            WaitUntil(() => !p.IsRunning);
            Assert.That(f.Started.Count(c => c.StartsWith("vecchio")), Is.EqualTo(3), "il vecchio non avvia altre pagine");
            Assert.That(f.Started.Count(c => c.StartsWith("nuovo")), Is.EqualTo(10));
        }

        [Test]
        public void Stop_NonAvviaAltrePagine() {
            var f = new FakeLoads();
            var p = new Precacher(maxParallel: 2);
            p.Start(Codes("p", 10), f.Load);
            WaitUntil(() => f.Started.Count == 2);
            p.Stop();
            f.FinishAll();
            WaitUntil(() => !p.IsRunning);
            Assert.That(f.Started.Count, Is.EqualTo(2));
        }

        [Test]
        public void PaginaFallita_NonAvviaLeSuccessive() {
            // come prima: dopo un fallimento il precaricamento si ferma, niente raffica sul server
            var f = new FakeLoads();
            var p = new Precacher(maxParallel: 1);
            p.Start(Codes("p", 10), f.Load);
            WaitUntil(() => f.Started.Count == 1);
            f.Finish("p1", null);
            WaitUntil(() => !p.IsRunning);
            Assert.That(f.Started.ToArray(), Is.EqualTo(new[] { "p1" }));
        }

        [Test]
        public void PagineGiaInCache_TutteSubito() {
            var f = new FakeLoads { Instant = true };
            var p = new Precacher(maxParallel: 3);
            p.Start(Codes("p", 20), f.Load);
            WaitUntil(() => !p.IsRunning, 2000);
            Assert.That(f.Started.Count, Is.EqualTo(20));
        }

        [Test]
        public void DownloadBloccante_ComeQuelloVero_RestaInParallelo() {
            // loadImage e' sincrono fino alla risposta del server (.Result in PageLoader): avviarlo nel
            // thread del ciclo bloccava il ciclo e il precaricamento tornava una pagina alla volta
            var p = new Precacher(maxParallel: 3);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            p.Start(Codes("p", 6), c => { Thread.Sleep(500); return Task.FromResult(c); });
            WaitUntil(() => !p.IsRunning, 5000);
            Assert.That(sw.Elapsed.TotalSeconds, Is.LessThan(1.6), "6 pagine da 0,5 s, 3 insieme: ~1 s (in fila 3 s)");
        }

        [Test]
        public void ListaVuota_Niente() {
            var p = new Precacher();
            Assert.That(() => p.Start(new string[0], c => Task.FromResult("x")), Throws.Nothing);
            WaitUntil(() => !p.IsRunning);
        }

        [Test]
        public void ErroreNelCaricamento_NonFermaIlProcessoEFermaIlPrecaricamento() {
            var p = new Precacher(maxParallel: 1);
            var started = new ConcurrentQueue<string>();
            p.Start(Codes("p", 5), c => { started.Enqueue(c); return Task.FromException<string>(new InvalidOperationException("boom")); });
            WaitUntil(() => !p.IsRunning);
            Assert.That(started.Count, Is.EqualTo(1));
        }
    }
}
