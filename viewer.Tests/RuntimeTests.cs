using NUnit.Framework;
using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace viewer.Tests {

    /// <summary>
    /// Download in parallelo verso lo stesso host. .NET Framework concede per default 2 connessioni
    /// per host, ma il limite vale per "gruppo di connessioni" e ogni HttpClientHandler ne ha uno
    /// suo: PageLoader crea un handler per richiesta, quindi le richieste NON si accodano.
    /// Questi test lo fissano, perche' passare a un HttpClient condiviso introdurrebbe la coda.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class RuntimeTests {
        LocalHttpServer server;
        int savedLimit;

        [SetUp]
        public void SetUp() {
            savedLimit = ServicePointManager.DefaultConnectionLimit;
            server = new LocalHttpServer { Concurrent = true };
            // risposta lenta come quella del server IIIF sotto carico (qui 1 s invece di 15-120)
            server.Handler = _ => { Thread.Sleep(1000); return ("HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\n", Encoding.ASCII.GetBytes("ok")); };
        }

        [TearDown]
        public void TearDown() {
            server.Dispose();
            ServicePointManager.DefaultConnectionLimit = savedLimit;
        }

        /// <summary>
        /// 4 richieste insieme verso lo stesso host; restituisce la durata totale.
        /// Verso 127.0.0.1 .NET non applica DefaultConnectionLimit (loopback senza limite): per
        /// riprodurre un host remoto come iiif-antenati il limite va imposto sul ServicePoint.
        /// </summary>
        TimeSpan QuattroRichiesteInsieme(Func<int, Task> richiesta) {
            ServicePointManager.FindServicePoint(new Uri(server.BaseUrl)).ConnectionLimit = 2;
            var sw = Stopwatch.StartNew();
            Task.WaitAll(Enumerable.Range(0, 4).Select(richiesta).ToArray());
            return sw.Elapsed;
        }

        [Test]
        public void PageLoader_QuattroRichiesteInsieme_NonSiAccodano() {
            var t = QuattroRichiesteInsieme(i => Task.Run(() => PageLoader.getResponseMessage(server.BaseUrl + "img" + i)?.Dispose()));
            Assert.That(server.MaxConcurrent, Is.EqualTo(4));
            Assert.That(t.TotalSeconds, Is.LessThan(1.8), "un solo turno: le 4 richieste partono insieme");
        }

        [Test]
        public void HttpClientCondiviso_ConLimite2_SiAccoderebbe() {
            // Controprova: con un solo HttpClient (passo 2 della revisione) il limite di 2 si sentirebbe.
            // Se un giorno PageLoader usera' un client condiviso, va alzato DefaultConnectionLimit.
            using (var shared = new System.Net.Http.HttpClient()) {
                var t = QuattroRichiesteInsieme(i => shared.GetAsync(server.BaseUrl + "img" + i));
                Assert.That(server.MaxConcurrent, Is.EqualTo(2));
                Assert.That(t.TotalSeconds, Is.GreaterThan(1.9), "4 richieste da 1 s su 2 connessioni: due turni");
            }
        }
    }
}
