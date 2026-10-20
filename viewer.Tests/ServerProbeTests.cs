using NUnit.Framework;
using System;
using System.Linq;
using System.Text;
using System.Threading;

namespace viewer.Tests {

    /// <summary>
    /// Pulsante "Diagnosi tempi" (pannello comandi): misura sito, manifest, un'immagine gia' vista,
    /// due pagine mai scaricate (modo del programma e modo del sito, a tasselli) e riassume gli ultimi
    /// download del programma, con una conclusione in parole semplici.
    /// </summary>
    [TestFixture, NonParallelizable, CancelAfter(30000)]
    public class ServerProbeTests {
        LocalHttpServer server;

        [SetUp]
        public void SetUp() {
            Tree.Reset();
            server = new LocalHttpServer { Concurrent = true };
        }

        [TearDown]
        public void TearDown() { server.Dispose(); }

        static byte[] Jpeg = Encoding.ASCII.GetBytes(new string('x', 20000));
        static (string, byte[]) Ok(string type, byte[] body) => ($"HTTP/1.1 200 OK\r\nContent-Type: {type}\r\n", body);
        static (string, byte[]) Info(int w, int h) => Ok("application/json", Encoding.UTF8.GetBytes(
            $"{{\"width\":{w},\"height\":{h},\"sizes\":[{{\"width\":{w / 4},\"height\":{h / 4}}},{{\"width\":{w / 2},\"height\":{h / 2}}}],\"tiles\":[{{\"width\":1024,\"height\":1024,\"scaleFactors\":[1,2,4,8,16]}}]}}"));

        ServerProbe.Options Opt(int fastMs = 3000, int slowMs = 20000) => new ServerProbe.Options {
            SiteUrl = server.BaseUrl,
            ManifestUrl = server.BaseUrl + "containers/m1/manifest",
            IiifBase = server.BaseUrl + "iiif/2/",
            Width = 1000,
            ViewedCode = "VISTA",
            ColdCodes = new[] { "NUOVA1", "NUOVA2" },
            FastMs = fastMs,
            SlowMs = slowMs,
        };

        void Handler(int imageDelayMs = 0, int status = 200) {
            server.Handler = req => {
                string path = req.Split(' ')[1];
                if (path.EndsWith("info.json")) return Info(3789, 2784);
                if (path.Contains("/iiif/2/")) {
                    Thread.Sleep(imageDelayMs);
                    return status == 200 ? Ok("image/jpeg", Jpeg) : ($"HTTP/1.1 {status} Errore\r\n", new byte[0]);
                }
                if (path.Contains("manifest")) return Ok("application/json", Encoding.UTF8.GetBytes("{}"));
                return Ok("text/html", Encoding.UTF8.GetBytes("<html>home</html>"));
            };
        }

        [Test]
        public void Tasselli_ComeIlVisualizzatoreDelSito() {
            // l'esempio reale: LPgEKzY, 3789x2784, tasselli 1024 a scala 2
            var t = ServerProbe.TileRegions(3789, 2784, 1024, 2);
            Assert.That(t, Is.EqualTo(new[] {
                "0,0,2048,2048/1024,", "2048,0,1741,2048/871,", "0,2048,2048,736/1024,", "2048,2048,1741,736/871," }));
        }

        [Test]
        public void ServerVeloce_TuttoOk_ConclusioneVeloce() {
            Handler();
            var r = ServerProbe.Run(Opt(), new DownloadStats());
            Assert.That(r.Steps.All(s => s.Ok), Is.True, r.Text);
            Assert.That(r.Steps.Select(s => s.Name), Is.EqualTo(new[] {
                "Sito antenati", "Manifest del registro", "Immagine gia' vista", "Pagina nuova (modo del programma)", "Pagina nuova (tasselli, come il sito)" }));
            Assert.That(r.Verdict, Does.StartWith("Server veloce"));
            Assert.That(server.Requests.Count(q => q.Contains("/NUOVA2/") && !q.Contains("info.json")), Is.EqualTo(4), "4 tasselli");
        }

        [Test]
        public void ServerLento_ConclusioneLento() {
            Handler(imageDelayMs: 600);
            var r = ServerProbe.Run(Opt(fastMs: 200, slowMs: 5000), new DownloadStats());
            Assert.That(r.Verdict, Does.StartWith("Server lento"));
        }

        [Test]
        public void ServerMoltoLento_ConclusioneMoltoLento() {
            Handler(imageDelayMs: 600);
            var r = ServerProbe.Run(Opt(fastMs: 100, slowMs: 300), new DownloadStats());
            Assert.That(r.Verdict, Does.StartWith("Server molto lento"));
        }

        [Test]
        public void ErroriDelServer_Riportati() {
            Handler(status: 500);
            var r = ServerProbe.Run(Opt(), new DownloadStats());
            Assert.That(r.Steps.Single(s => s.Name == "Immagine gia' vista").Status, Is.EqualTo(500));
            Assert.That(r.Verdict, Does.Contain("errori"));
        }

        [Test]
        public void SitoIrraggiungibile_DettoChiaramente() {
            server.Dispose();
            var r = ServerProbe.Run(Opt(), new DownloadStats());
            Assert.That(r.Verdict, Does.StartWith("Sito non raggiungibile"));
        }

        [Test]
        public void TasselliMoltoPiuVeloci_SuggeritoNellaConclusione() {
            // pagina intera lenta, tasselli veloci: e' il caso in cui converrebbe scaricare come il sito
            server.Handler = req => {
                string path = req.Split(' ')[1];
                if (path.EndsWith("info.json")) return Info(3789, 2784);
                if (path.Contains("/full/")) { Thread.Sleep(1500); return Ok("image/jpeg", Jpeg); }
                if (path.Contains("/iiif/2/")) return Ok("image/jpeg", Jpeg);
                return Ok("text/html", Encoding.UTF8.GetBytes("ok"));
            };
            var r = ServerProbe.Run(Opt(), new DownloadStats());
            Assert.That(r.Text, Does.Contain("tasselli"));
            Assert.That(r.TilesFaster, Is.True);
        }

        [Test]
        public void SenzaPagineNuove_SaltaQuelPasso() {
            Handler();
            var o = Opt(); o.ColdCodes = new string[0];
            var r = ServerProbe.Run(o, new DownloadStats());
            Assert.That(r.Steps.Count, Is.EqualTo(3));
        }

        // ---------- ultimi download del programma ----------

        [Test]
        public void Statistiche_UltimiVenti() {
            var st = new DownloadStats(capacity: 20);
            for (int i = 0; i < 25; i++) st.Add("c" + i, 200, coda: 0, server: 1000 + i);
            Assert.That(st.Snapshot().Length, Is.EqualTo(20));
            Assert.That(st.Snapshot().First().Code, Is.EqualTo("c5"));
        }

        [Test]
        public void Statistiche_NelRiepilogo_TempiErroriCodaAnnullati() {
            Handler();
            var st = new DownloadStats();
            st.Add("a", 200, coda: 0, server: 2000);
            st.Add("b", 200, coda: 4000, server: 6000);
            st.Add("c", 500, coda: 0, server: 3000);
            st.Add("d", 524, coda: 0, server: 125000);
            st.AddCancelled("e");
            var r = ServerProbe.Run(Opt(), st);
            Assert.That(r.Text, Does.Contain("Ultimi 5 download"));
            Assert.That(r.Text, Does.Contain("HTTP 500: 1").And.Contain("HTTP 524: 1"));
            Assert.That(r.Text, Does.Contain("annullati: 1"));
            Assert.That(r.Verdict, Does.Contain("coda"), "4 s in coda nel programma va segnalato");
        }
    }
}
