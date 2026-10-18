using NUnit.Framework;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace viewer.Tests {

    /// <summary>
    /// Un solo download in volo per immagine, riagganciabile e annullabile.
    /// Prima (existingRequests): chi chiedeva un'immagine gia' in download riceveva subito il percorso
    /// di un file non ancora scritto, e i download non si potevano annullare.
    /// </summary>
    [TestFixture, CancelAfter(10000)]
    public class InFlightDownloadsTests {

        /// <summary>download finto che finisce quando il test lo decide</summary>
        sealed class FakeDownload {
            public readonly TaskCompletionSource<string> Done = new TaskCompletionSource<string>();
            public int Starts;
            public CancellationToken Token;
            public Task<string> Start(CancellationToken ct) {
                Interlocked.Increment(ref Starts);
                Token = ct;
                ct.Register(() => Done.TrySetResult(null));
                return Done.Task;
            }
        }

        [Test]
        public void StessaImmagine_RiagganciaIlDownloadInCorso() {
            var d = new InFlightDownloads();
            var f = new FakeDownload();
            var t1 = d.GetOrStart("A", f.Start);
            var t2 = d.GetOrStart("A", f.Start);
            Assert.That(f.Starts, Is.EqualTo(1));
            Assert.That(t2, Is.SameAs(t1));
            Assert.That(t2.IsCompleted, Is.False, "nessun percorso finche' il file non c'e'");
            f.Done.SetResult("data\\A.jpg");
            Assert.That(t2.Result, Is.EqualTo("data\\A.jpg"));
        }

        [Test]
        public void DownloadFinito_LaRichiestaDopoNeAvviaUnoNuovo() {
            var d = new InFlightDownloads();
            var f1 = new FakeDownload();
            var t1 = d.GetOrStart("A", f1.Start);
            f1.Done.SetResult(null);              // fallito
            Assert.That(t1.Result, Is.Null);      // la voce si libera prima che il risultato sia visibile
            Assert.That(d.IsInFlight("A"), Is.False);
            var f2 = new FakeDownload();
            d.GetOrStart("A", f2.Start);
            Assert.That(f2.Starts, Is.EqualTo(1));
        }

        [Test]
        public void Annullato_TokenSegnalatoEVoceTolta() {
            var d = new InFlightDownloads();
            var f = new FakeDownload();
            var t = d.GetOrStart("A", f.Start);
            Assert.That(d.Cancel("A"), Is.True);
            Assert.That(f.Token.IsCancellationRequested, Is.True);
            Assert.That(t.Result, Is.Null);
            Assert.That(d.IsInFlight("A"), Is.False);
        }

        [Test]
        public void Annullato_UnaNuovaRichiestaNonRiagganciaQuelloAnnullato() {
            // Go = annulla e richiedi: la richiesta nuova non deve ricevere il risultato annullato
            var d = new InFlightDownloads();
            var vecchio = new TaskCompletionSource<string>();
            d.GetOrStart("A", ct => vecchio.Task);   // non reagisce subito all'annullamento
            d.Cancel("A");
            var f = new FakeDownload();
            var t = d.GetOrStart("A", f.Start);
            Assert.That(f.Starts, Is.EqualTo(1));
            vecchio.SetResult(null);                  // il vecchio finisce dopo: non tocca il nuovo
            Assert.That(d.IsInFlight("A"), Is.True);
            f.Done.SetResult("ok");
            Assert.That(t.Result, Is.EqualTo("ok"));
        }

        [Test]
        public void AnnullaTranne_TieneSoloQuelliRichiesti() {
            var d = new InFlightDownloads();
            var fs = new[] { "A", "B", "C" }.ToDictionary(k => k, _ => new FakeDownload());
            foreach (var kv in fs) d.GetOrStart(kv.Key, kv.Value.Start);

            int n = d.CancelAllExcept(k => k == "B");

            Assert.That(n, Is.EqualTo(2));
            Assert.That(fs["A"].Token.IsCancellationRequested, Is.True);
            Assert.That(fs["B"].Token.IsCancellationRequested, Is.False);
            Assert.That(fs["C"].Token.IsCancellationRequested, Is.True);
            Assert.That(d.IsInFlight("B"), Is.True);
        }

        [Test]
        public void AnnullaDiImmagineNonInVolo_False() {
            Assert.That(new InFlightDownloads().Cancel("X"), Is.False);
        }

        [Test]
        public void DownloadConErrore_VoceTolta() {
            var d = new InFlightDownloads();
            var t = d.GetOrStart("A", ct => Task.FromException<string>(new InvalidOperationException("boom")));
            Assert.That(() => t.Wait(), Throws.InstanceOf<AggregateException>());
            Assert.That(d.IsInFlight("A"), Is.False);
        }

        [Test]
        public void RichiesteConcorrenti_UnSoloAvvio() {
            var d = new InFlightDownloads();
            var f = new FakeDownload();
            using (var go = new ManualResetEventSlim(false)) {
                // StartNew e non Task.Run: Task.Run "srotolerebbe" la Task restituita e aspetterebbe il download
                var tasks = Enumerable.Range(0, 32)
                    .Select(_ => Task.Factory.StartNew(() => { go.Wait(); return d.GetOrStart("A", f.Start); }, TaskCreationOptions.LongRunning))
                    .ToArray();
                go.Set();
                Task.WaitAll(tasks);
                Assert.That(f.Starts, Is.EqualTo(1));
                Assert.That(tasks.Select(x => x.Result).Distinct().Count(), Is.EqualTo(1));
            }
            f.Done.SetResult("ok");
        }
    }
}
