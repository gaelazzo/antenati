using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace viewer.Tests {

    /// <summary>
    /// Download di immagini e manifest contro un server locale che imita iiif-antenati / dam-antenati,
    /// e risoluzione degli indirizzi abbreviati (bit.ly &amp; co.) usati per le pagine "importanti".
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA)]
    public class DownloadTests {
        LocalHttpServer server;
        Viewer v;
        IiifServer savedIiif;
        string savedDam;

        [SetUp]
        public void SetUp() {
            Tree.Reset();
            server = new LocalHttpServer();
            savedIiif = IiifServer.current;
            savedDam = ManifestLoader.damBaseUrl;
            IiifServer.current = new IiifServer(server.BaseUrl + "iiif/2/");
            ManifestLoader.damBaseUrl = server.BaseUrl + "containers/";

            v = Priv.NewViewer();
            Priv.Set(v, "currExploring", "");
        }

        [TearDown]
        public void TearDown() {
            IiifServer.current = savedIiif;
            ManifestLoader.damBaseUrl = savedDam;
            server.Dispose();
        }

        // ---------- risposte del server finto ----------

        static readonly byte[] Jpeg = MakeJpeg(400, 400);
        static readonly byte[] TinyJpeg = MakeJpeg(20, 20);

        static byte[] MakeJpeg(int w, int h) {
            var rnd = new Random(1);
            using (var b = new Bitmap(w, h)) {
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        b.SetPixel(x, y, Color.FromArgb(rnd.Next(256), rnd.Next(256), rnd.Next(256)));
                using (var ms = new MemoryStream()) {
                    b.Save(ms, ImageFormat.Jpeg);
                    return ms.ToArray();
                }
            }
        }

        static (string, byte[]) Image(byte[] body) => ("HTTP/1.1 200 OK\r\nContent-Type: image/jpeg\r\n", body);
        static (string, byte[]) Status(int code, string reason) => ($"HTTP/1.1 {code} {reason}\r\nContent-Type: text/html\r\n", Encoding.UTF8.GetBytes("<html>" + reason + "</html>"));

        static string Path0(string request) => request.Split(' ')[1];

        string[] RequestedPaths() => server.Requests.Select(Path0).ToArray();

        static string NewCode() => "w" + Guid.NewGuid().ToString("N").Substring(0, 6) + "Ab";

        bool Download(string code, string path) => Priv.Call<System.Threading.Tasks.Task<bool>>(v, "queueDownloadFile", code, path, true).Result;

        // ---------- immagini: formati e fallback ----------

        [Test]
        public void Immagine_LarghezzaRichiesta() {
            Priv.Set(v, "dimension", 1600);
            server.Handler = _ => Image(Jpeg);
            string path = Path.Combine(Sandbox.NewDir(), "a.jpg");

            Assert.That(Download("LqaVpAR", path), Is.True);
            Assert.That(RequestedPaths(), Is.EqualTo(new[] { "/iiif/2/LqaVpAR/full/1600,/0/default.jpg" }));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(Jpeg));
        }

        [Test]
        public void Immagine_DimensioneZero_ChiedeMax() {
            Priv.Set(v, "dimension", 0);
            server.Handler = _ => Image(Jpeg);
            Assert.That(Download("LqaVpAR", Path.Combine(Sandbox.NewDir(), "a.jpg")), Is.True);
            Assert.That(RequestedPaths(), Is.EqualTo(new[] { "/iiif/2/LqaVpAR/full/max/0/default.jpg" }));
        }

        [Test]
        public void Immagine_403PerUpscaling_RitentaAMax() {
            // Cantaloupe rifiuta con 403 una larghezza maggiore di quella nativa (pagine piccole, copertine)
            Priv.Set(v, "dimension", 3000);
            server.Handler = req => Path0(req).Contains("/full/3000,/") ? Status(403, "Forbidden") : Image(Jpeg);

            Assert.That(Download("LqaVpAR", Path.Combine(Sandbox.NewDir(), "a.jpg")), Is.True);
            Assert.That(RequestedPaths(), Is.EqualTo(new[] {
                "/iiif/2/LqaVpAR/full/3000,/0/default.jpg",
                "/iiif/2/LqaVpAR/full/max/0/default.jpg" }));
        }

        [Test]
        public void Immagine_403AncheAMax_Fallisce() {
            Priv.Set(v, "dimension", 0);
            server.Handler = _ => Status(403, "Forbidden");
            Assert.That(Download("LqaVpAR", Path.Combine(Sandbox.NewDir(), "a.jpg")), Is.False);
            Assert.That(server.Requests.Count, Is.EqualTo(1), "a dimensione nativa non c'e' un altro formato da provare");
        }

        [Test]
        public void Immagine_HeaderDiHotlinkProtection() {
            server.Handler = _ => Image(Jpeg);
            Download("LqaVpAR", Path.Combine(Sandbox.NewDir(), "a.jpg"));
            server.Requests.TryPeek(out var req);
            Assert.That(req, Does.Contain("Referer: https://antenati.cultura.gov.it"));
            Assert.That(req, Does.Contain("Sec-Fetch-Site: same-site"));
        }

        [TestCase(404, "Not Found")]
        [TestCase(500, "Internal Server Error")]
        public void Immagine_ErroreHttp_NessunFile(int code, string reason) {
            server.Handler = _ => Status(code, reason);
            string path = Path.Combine(Sandbox.NewDir(), "a.jpg");
            Assert.That(Download("LqaVpAR", path), Is.False);
            Assert.That(File.Exists(path), Is.False);
        }

        [Test]
        public void Immagine_PaginaHtmlAlPostoDellImmagine_Scartata() {
            // es. pagina di blocco del WAF servita con 200
            string html = "<!DOCTYPE html><html>" + new string('x', 20000) + "</html>";
            server.Handler = _ => ("HTTP/1.1 200 OK\r\nContent-Type: text/html\r\n", Encoding.UTF8.GetBytes(html));
            string path = Path.Combine(Sandbox.NewDir(), "a.jpg");
            Assert.That(Download("LqaVpAR", path), Is.False);
            Assert.That(File.Exists(path), Is.False);
        }

        [Test]
        public void Immagine_TroppoPiccola_Scartata() {
            server.Handler = _ => Image(TinyJpeg);
            string path = Path.Combine(Sandbox.NewDir(), "a.jpg");
            Assert.That(TinyJpeg.Length, Is.LessThan(10000));
            Assert.That(Download("LqaVpAR", path), Is.False);
            Assert.That(File.Exists(path), Is.False);
        }

        [Test]
        public void Immagine_CreaLaCartellaDestinazione() {
            server.Handler = _ => Image(Jpeg);
            string path = Path.Combine(Sandbox.NewDir(), "nuova", "a.jpg");
            Assert.That(Download("LqaVpAR", path), Is.True);
            Assert.That(File.Exists(path), Is.True);
        }

        [Test]
        public void Immagine_TimeoutPredefinito_PocoOltreIlTaglioDiCloudflare() {
            // Cloudflare chiude con 524 intorno ai 125 s: aspettare di piu' tiene solo occupato il
            // download (prima era 5 minuti); meno di 100 s (default .NET) perderebbe risposte lente valide
            var t = (TimeSpan)typeof(Viewer).GetField("imageTimeout", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
            Assert.That(t.TotalSeconds, Is.InRange(130, 180));
        }

        [Test]
        public void Immagine_ServerTroppoLento_TimeoutDelleImmagini() {
            var field = typeof(Viewer).GetField("imageTimeout", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var saved = field.GetValue(null);
            field.SetValue(null, TimeSpan.FromSeconds(1));
            try {
                server.Concurrent = true;
                server.Handler = _ => { Thread.Sleep(3000); return Image(Jpeg); };
                string path = Path.Combine(Sandbox.NewDir(), "a.jpg");
                var sw = System.Diagnostics.Stopwatch.StartNew();
                Assert.That(Download("LqaVpAR", path), Is.False);
                Assert.That(sw.Elapsed.TotalSeconds, Is.LessThan(2.5), "queueDownloadFile deve usare imageTimeout");
                Assert.That(File.Exists(path), Is.False);
            }
            finally { field.SetValue(null, saved); }
        }

        [Test]
        public void Immagine_ServerIrraggiungibile_False() {
            server.Dispose();
            Assert.That(Download("LqaVpAR", Path.Combine(Sandbox.NewDir(), "a.jpg")), Is.False);
        }

        // ---------- loadImage: cache locale in data\ ----------

        void PrepareRegister(params string[] codes) {
            Tree.Bari();
            Tree.AddAnno();
            Tree.AddKind();
            Tree.AddRegistro();
            Priv.Set(v, "idRegistro", "282248");
            var mapPage = new Dictionary<int, string>();
            var pageDecode = new Dictionary<string, int>();
            for (int i = 0; i < codes.Length; i++) { mapPage[i + 1] = codes[i]; pageDecode[codes[i]] = i + 1; }
            Priv.Set(v, "mapPage", mapPage);
            Priv.Set(v, "pageDecode", pageDecode);
            Priv.Set(v, "savedFiles", new HashSet<string>());
            Priv.Set(v, "failCount", new Dictionary<string, int>());
            Priv.Set(v, "Sdownload", new Semaphore(5, 20));
            Priv.Set(v, "downloads", new InFlightDownloads());
            Priv.Set(v, "cacheSize", 10);
        }

        // ---------- download in corso: riaggancio, annullamento, ricarica forzata ----------

        static void WaitUntil(Func<bool> cond, int ms = 5000) {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!cond()) {
                if (sw.ElapsedMilliseconds > ms) Assert.Fail("condizione non raggiunta entro " + ms + " ms");
                Thread.Sleep(20);
            }
        }

        System.Threading.Tasks.Task<string> LoadImageAsync(string code) =>
            System.Threading.Tasks.Task.Factory.StartNew(() => LoadImage(code), System.Threading.Tasks.TaskCreationOptions.LongRunning);

        Dictionary<string, int> Fails => (Dictionary<string, int>)Priv.Get(v, "failCount");

        [Test, CancelAfter(15000)]
        public void LoadImage_StessaImmagineInParallelo_UnSoloDownloadEFileGiaPronto() {
            // Prima: il secondo chiamante riceveva subito il percorso di un file non ancora scritto
            string code = NewCode();
            PrepareRegister(code);
            server.Concurrent = true;
            server.Handler = _ => { Thread.Sleep(1500); return Image(Jpeg); };

            var first = LoadImageAsync(code);
            WaitUntil(() => server.Requests.Count == 1);
            var second = LoadImageAsync(code);
            string p2 = second.Result;
            bool existedWhenReturned = p2 != null && File.Exists(p2);

            Assert.That(existedWhenReturned, Is.True, "il percorso va restituito quando il file c'e'");
            Assert.That(first.Result, Is.EqualTo(p2));
            Assert.That(server.Requests.Count, Is.EqualTo(1), "il secondo riaggancia il primo download");
        }

        [Test, CancelAfter(15000)]
        public void LoadImage_PaginaLontanaAnnullata_NullSubitoESenzaFallimento() {
            var codes = Enumerable.Range(0, 60).Select(_ => NewCode()).ToArray();
            PrepareRegister(codes);
            server.Concurrent = true;
            server.Handler = _ => { Thread.Sleep(5000); return Image(Jpeg); };

            var load = LoadImageAsync(codes[0]);           // pagina 1
            WaitUntil(() => server.Requests.Count == 1);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Priv.Call(v, "keepDownloadsNear", 50);           // l'utente e' saltato a pagina 50

            Assert.That(load.Result, Is.Null);
            Assert.That(sw.Elapsed.TotalSeconds, Is.LessThan(2), "annullato, non atteso fino alla fine");
            Assert.That(Fails.ContainsKey(codes[0]), Is.False, "un annullamento non e' un fallimento del sito");
            Assert.That(File.Exists(Path.Combine("data", Priv.Call<string>(v, "extendCode", codes[0] + ".jpg"))), Is.False);

            // tornando sulla pagina, il download riparte
            server.Handler = _ => Image(Jpeg);
            Assert.That(LoadImage(codes[0]), Is.Not.Null);
            Assert.That(server.Requests.Count, Is.EqualTo(2));
        }

        [Test, CancelAfter(15000)]
        public void LoadImage_PaginaVicina_NonAnnullata() {
            var codes = Enumerable.Range(0, 60).Select(_ => NewCode()).ToArray();
            PrepareRegister(codes);
            server.Concurrent = true;
            server.Handler = _ => { Thread.Sleep(1500); return Image(Jpeg); };

            var load = LoadImageAsync(codes[2]);           // pagina 3 (precache)
            WaitUntil(() => server.Requests.Count == 1);
            Priv.Call(v, "keepDownloadsNear", 5);            // l'utente va avanti di due pagine

            Assert.That(load.Result, Is.Not.Null);
            Assert.That(server.Requests.Count, Is.EqualTo(1));
        }

        [Test, CancelAfter(15000)]
        public void LoadImage_AnnullatoMentreAspettaIlSemaforo_EsceSenzaSbilanciarlo() {
            string code = NewCode();
            PrepareRegister(code);
            var sem = new Semaphore(0, 20);                  // tutti i posti occupati
            Priv.Set(v, "Sdownload", sem);
            server.Handler = _ => Image(Jpeg);

            var load = LoadImageAsync(code);
            WaitUntil(() => ((InFlightDownloads)Priv.Get(v, "downloads")).IsInFlight(code));
            Thread.Sleep(200);
            ((InFlightDownloads)Priv.Get(v, "downloads")).Cancel(code);

            Assert.That(load.Wait(2000), Is.True, "non deve restare appeso al semaforo");
            Assert.That(load.Result, Is.Null);
            Assert.That(server.Requests, Is.Empty);
            Assert.That(sem.WaitOne(0), Is.False, "nessun Release senza WaitOne riuscito");
        }

        [Test, CancelAfter(15000)]
        public void RicaricaForzata_DopoTreFallimenti_AzzeraERichiede() {
            string code = NewCode();
            PrepareRegister(code);
            server.Handler = _ => Status(500, "Internal Server Error");
            for (int i = 0; i < 3; i++) LoadImage(code);
            Assert.That(Fails[code], Is.EqualTo(3));
            Priv.Set(v, "nEffettivo", 1);                    // il timer l'aveva data per "mostrata"

            Priv.Call(v, "forceReload", 1);

            Assert.That(Fails.ContainsKey(code), Is.False);
            Assert.That((int)Priv.Get(v, "nEffettivo"), Is.EqualTo(0), "cosi' il timer la richiede");
            server.Handler = _ => Image(Jpeg);
            Assert.That(LoadImage(code), Is.Not.Null);
        }

        // ---------- precaricamento ----------

        void PreparePrecache(int cacheSize = 5) {
            Priv.Set(v, "standardStep", 1);
            Priv.Set(v, "chkEnableKey", new System.Windows.Forms.CheckBox());
            Priv.Set(v, "txtCacheSize", new System.Windows.Forms.TextBox { Text = cacheSize.ToString() });
            Priv.Set(v, "precacher", new Precacher());
        }

        void SwitchRegister(string[] codes) {
            var mapPage = new Dictionary<int, string>();
            var pageDecode = new Dictionary<string, int>();
            for (int i = 0; i < codes.Length; i++) { mapPage[i + 1] = codes[i]; pageDecode[codes[i]] = i + 1; }
            Priv.Set(v, "mapPage", mapPage);
            Priv.Set(v, "pageDecode", pageDecode);
        }

        [Test, CancelAfter(20000)]
        public void Precache_CambioRegistro_IlNuovoParteSubito() {
            // Doppio click su un altro registro mentre il precaricamento del precedente e' in corso:
            // prima il nuovo non partiva ("isCaching") e con +1 le pagine arrivavano una ogni 20 s
            var vecchio = Enumerable.Range(0, 30).Select(_ => NewCode()).ToArray();
            var nuovo = Enumerable.Range(0, 30).Select(_ => NewCode()).ToArray();
            PrepareRegister(vecchio);
            PreparePrecache();
            server.Concurrent = true;
            server.Handler = _ => { Thread.Sleep(3000); return Image(Jpeg); };

            Priv.Call(v, "startPrecache", 1, true);
            WaitUntil(() => server.Requests.Count >= 1);

            SwitchRegister(nuovo);
            Priv.Call(v, "keepDownloadsNear", 1);
            Priv.Call(v, "startPrecache", 2, true);

            WaitUntil(() => server.Requests.Any(r => r.Contains(nuovo[1])), 1000);
        }

        [Test, CancelAfter(20000)]
        public void Precache_TrePagineInParallelo() {
            var codes = Enumerable.Range(0, 30).Select(_ => NewCode()).ToArray();
            PrepareRegister(codes);
            PreparePrecache(cacheSize: 6);
            server.Concurrent = true;
            server.Handler = _ => { Thread.Sleep(1000); return Image(Jpeg); };

            var sw = System.Diagnostics.Stopwatch.StartNew();
            Priv.Call(v, "startPrecache", 2, true);
            WaitUntil(() => codes.Skip(1).Take(6).All(c => File.Exists(Path.Combine("data", Priv.Call<string>(v, "extendCode", c + ".jpg")))), 8000);

            Assert.That(server.MaxConcurrent, Is.EqualTo(3));
            Assert.That(sw.Elapsed.TotalSeconds, Is.LessThan(4), "6 pagine da 1 s a 3 per volta: ~2 s (una alla volta sarebbero 6)");
        }

        [Test]
        public void Diagnosi_PagineScelteBene() {
            // gia' vista = pagina a video; nuove = dalla fine del registro, ne' in cache ne' in download
            var c = Enumerable.Range(0, 6).Select(_ => NewCode()).ToArray();
            PrepareRegister(c);
            Priv.Set(v, "dimension", 1200);
            Priv.Set(v, "nEffettivo", 2);
            Directory.CreateDirectory("data");
            File.WriteAllBytes(Path.Combine("data", Priv.Call<string>(v, "extendCode", c[5] + ".jpg")), new byte[20000]);
            var gate = new System.Threading.Tasks.TaskCompletionSource<string>();
            ((InFlightDownloads)Priv.Get(v, "downloads")).GetOrStart(c[4], _ => gate.Task);
            try {
                var o = Priv.Call<ServerProbe.Options>(v, "buildDiagnosisOptions");
                Assert.That(o.ViewedCode, Is.EqualTo(c[1]));
                Assert.That(o.ColdCodes, Is.EqualTo(new[] { c[3], c[2] }), "pagina 6 in cache, pagina 5 in download");
                Assert.That(o.Width, Is.EqualTo(1200));
                Assert.That(o.ManifestUrl, Is.Null, "registro senza manifestId");
            }
            finally { gate.SetResult(null); }
        }

        [Test]
        public void Diagnosi_RegistraIDownload() {
            string code = NewCode();
            PrepareRegister(code);
            var st = new DownloadStats();
            Priv.Set(v, "downloadStats", st);
            server.Handler = _ => Status(500, "Internal Server Error");
            LoadImage(code);
            server.Handler = _ => Image(Jpeg);
            LoadImage(code);
            var e = st.Snapshot();
            Assert.That(e.Select(x => x.Status), Is.EqualTo(new[] { 500, 200 }));
        }

        [Test]
        public void UltimaPaginaVista_SalvataQuandoVaAVideo() {
            PrepareRegister("a", "b", "c");
            Priv.Call(v, "rememberPageShown", 3);
            Assert.That(Registro.registroById["282248"].lastPageViewed, Is.EqualTo("3"));

            string f = Path.Combine(Sandbox.NewDir(), "r.json");
            Registro.Save(f);
            Registro.registri.Clear(); Registro.registroById.Clear();
            Registro.Load(f);
            Assert.That(Registro.registroById["282248"].lastPageViewed, Is.EqualTo("3"), "riaperto col doppio click va li'");
        }

        [Test, CancelAfter(15000)]
        public void RicaricaForzata_AnnullaIlDownloadInCorso() {
            string code = NewCode();
            PrepareRegister(code);
            server.Concurrent = true;
            server.Handler = _ => { Thread.Sleep(5000); return Image(Jpeg); };
            var load = LoadImageAsync(code);
            WaitUntil(() => server.Requests.Count == 1);

            Priv.Call(v, "forceReload", 1);

            Assert.That(load.Wait(2000), Is.True);
            Assert.That(((InFlightDownloads)Priv.Get(v, "downloads")).IsInFlight(code), Is.False);
        }

        string LoadImage(string code) => Priv.Call<System.Threading.Tasks.Task<string>>(v, "loadImage", code).Result;
        HashSet<string> SavedFiles => (HashSet<string>)Priv.Get(v, "savedFiles");

        [Test]
        public void LoadImage_ScaricaESalvaInData() {
            string code = NewCode();
            PrepareRegister(code);
            server.Handler = _ => Image(Jpeg);

            string path = LoadImage(code);

            Assert.That(path, Is.EqualTo(Path.Combine("data", Priv.Call<string>(v, "extendCode", code + ".jpg"))));
            Assert.That(File.Exists(path), Is.True);
            Assert.That(SavedFiles, Does.Contain(Priv.Call<string>(v, "extendCode", code)));
            WaitUntil(() => ((InFlightDownloads)Priv.Get(v, "downloads")).Count == 0);   // la richiesta in corso va tolta
        }

        [Test]
        public void LoadImage_GiaInCache_NessunaRichiesta() {
            string code = NewCode();
            PrepareRegister(code);
            server.Handler = _ => Image(Jpeg);
            LoadImage(code);
            while (server.Requests.TryDequeue(out _)) { }

            Assert.That(LoadImage(code), Is.Not.Null);
            Assert.That(server.Requests, Is.Empty);
        }

        [Test]
        public void LoadImage_VoceInCacheMaFileCancellato_Riscarica() {
            string code = NewCode();
            PrepareRegister(code);
            server.Handler = _ => Image(Jpeg);
            string path = LoadImage(code);
            File.Delete(path);
            while (server.Requests.TryDequeue(out _)) { }

            Assert.That(LoadImage(code), Is.EqualTo(path));
            Assert.That(File.Exists(path), Is.True);
            Assert.That(server.Requests.Count, Is.EqualTo(1));
        }

        [Test]
        public void LoadImage_Fallimento_ContatoPerFermareIRetry() {
            string code = NewCode();
            PrepareRegister(code);
            server.Handler = _ => Status(404, "Not Found");

            Assert.That(LoadImage(code), Is.Null);
            Assert.That(LoadImage(code), Is.Null);
            Assert.That(((Dictionary<string, int>)Priv.Get(v, "failCount"))[code], Is.EqualTo(2));
            Assert.That(SavedFiles, Is.Empty);
        }

        [Test]
        public void LoadImage_SuccessoDopoFallimenti_AzzeraIlContatore() {
            string code = NewCode();
            PrepareRegister(code);
            server.Handler = _ => Status(404, "Not Found");
            LoadImage(code);
            server.Handler = _ => Image(Jpeg);

            Assert.That(LoadImage(code), Is.Not.Null);
            Assert.That(((Dictionary<string, int>)Priv.Get(v, "failCount")).ContainsKey(code), Is.False);
        }

        static byte[] PlaceholderBmp() {
            // 90x90: sotto i 100 pixel ma oltre i 10.000 byte, cosi' supera il primo filtro sulla dimensione
            using (var b = new Bitmap(90, 90))
            using (var ms = new MemoryStream()) {
                b.Save(ms, ImageFormat.Bmp);
                return ms.ToArray();
            }
        }

        [Test]
        public void LoadImage_Segnaposto_NonRestaInCache() {
            string code = NewCode();
            PrepareRegister(code);
            byte[] placeholder = PlaceholderBmp();
            Assert.That(placeholder.Length, Is.GreaterThan(10000));
            server.Handler = _ => Image(placeholder);

            Assert.That(LoadImage(code), Is.Null);
            Assert.That(File.Exists(Path.Combine("data", Priv.Call<string>(v, "extendCode", code + ".jpg"))), Is.False);
            Assert.That(((Dictionary<string, int>)Priv.Get(v, "failCount"))[code], Is.EqualTo(1));

            // al tentativo successivo il sito manda l'immagine vera: deve arrivare quella
            server.Handler = _ => Image(Jpeg);
            string path = LoadImage(code);
            Assert.That(path, Is.Not.Null);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(Jpeg));
        }

        [Test]
        public void LoadImage_InParallelo_SavedFilesCompleto() {
            // come il caricamento della pagina e il precaricamento, che girano insieme su thread diversi
            var codes = Enumerable.Range(0, 60).Select(_ => NewCode()).ToArray();
            PrepareRegister(codes);
            server.Handler = _ => Image(Jpeg);

            var tasks = codes.Select(c => System.Threading.Tasks.Task.Run(() => LoadImage(c))).ToArray();
            System.Threading.Tasks.Task.WaitAll(tasks);

            Assert.That(tasks.Select(t => t.Result), Has.None.Null);
            Assert.That(SavedFiles.Count, Is.EqualTo(codes.Length));
            Assert.That(codes.All(c => SavedFiles.Contains(Priv.Call<string>(v, "extendCode", c))), Is.True);
        }

        [Test]
        public void LoadImage_CodiceFuoriDalManifest_Null() {
            PrepareRegister("aaa");
            Assert.That(LoadImage("zzz"), Is.Null);
            Assert.That(server.Requests, Is.Empty);
        }

        [Test]
        public void LoadImage_PerNumeroDiPagina() {
            string c1 = NewCode(), c2 = NewCode();
            PrepareRegister(c1, c2);
            server.Handler = _ => Image(Jpeg);

            string path = Priv.Call<System.Threading.Tasks.Task<string>>(v, "loadImage", 2).Result;
            Assert.That(path, Does.EndWith(Priv.Call<string>(v, "extendCode", c2 + ".jpg")));
            Assert.That(Priv.Call<System.Threading.Tasks.Task<string>>(v, "loadImage", 3).Result, Is.Null);
        }

        // ---------- manifest ----------

        static string ManifestJson(string idRegistro, params string[] codes) {
            var canvases = new JArray(codes.Select(c => new JObject {
                ["@id"] = $"https://antenati.cultura.gov.it/ark:/12657/an_ua{idRegistro}/{c}", ["width"] = 2000 }));
            return new JObject {
                ["sequences"] = new JArray { new JObject { ["canvases"] = canvases } },
                ["metadata"] = new JArray {
                    new JObject { ["label"] = "Conservato da", ["value"] = "Archivio di Stato di Bari" },
                    new JObject { ["label"] = "Contesto archivistico", ["value"] = "Archivio di Stato di Bari > Stato civile napoleonico > Bari" },
                    new JObject { ["label"] = "Titolo", ["value"] = "1813" },
                    new JObject { ["label"] = "Tipologia", ["value"] = "Nati" } }
            }.ToString();
        }

        [Test]
        public void Manifest_ScaricatoConHeaderCors() {
            server.Handler = _ => ("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n", Encoding.UTF8.GetBytes(ManifestJson("282248", "a", "b")));

            var json = ManifestLoader.LoadManifestJsonAsync("wOgD6pM").Result;

            Assert.That(json["sequences"][0]["canvases"].Count(), Is.EqualTo(2));
            server.Requests.TryPeek(out var req);
            Assert.That(Path0(req), Is.EqualTo("/containers/wOgD6pM/manifest"));
            Assert.That(req, Does.Contain("Origin: https://antenati.cultura.gov.it"));
            Assert.That(req, Does.Contain("Sec-Fetch-Mode: cors"));
        }

        [Test]
        public void Manifest_PrimoCaricamentoScaricaESalva_PoiUsaLaCache() {
            string id = "m" + Guid.NewGuid().ToString("N").Substring(0, 6);
            Directory.CreateDirectory("manifest");
            server.Handler = _ => ("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n", Encoding.UTF8.GetBytes(ManifestJson("282248", "wOgD6pM", "LqaVpAR")));

            var m = Manifest.LoadManifest(id).Result;
            Assert.That(m.mapPage[2], Is.EqualTo("LqaVpAR"));
            Assert.That(m.pageDecode["wOgD6pM"], Is.EqualTo(1));
            Assert.That(File.Exists(Manifest.manifestFileName(id)), Is.True);
            Assert.That(JObject.Parse(File.ReadAllText(Manifest.manifestFileName(id)))["Comune"].ToString(), Is.EqualTo("Bari"));

            // Nuova sessione: memoria vuota, il manifest arriva dal file senza rete
            Tree.Reset();
            while (server.Requests.TryDequeue(out _)) { }
            var again = Manifest.LoadManifest(id).Result;
            Assert.That(again.mapPage, Is.EqualTo(m.mapPage));
            Assert.That(server.Requests, Is.Empty);
        }

        // ---------- indirizzi abbreviati ----------

        [Test]
        public void ExpandUrl_SegueIlRedirect() {
            string target = server.BaseUrl + "ark:/12657/an_ua282248/LqaVpAR";
            server.Handler = req => Path0(req).StartsWith("/s/")
                ? ($"HTTP/1.1 301 Moved Permanently\r\nLocation: {target}\r\n", new byte[0])
                : ("HTTP/1.1 200 OK\r\n", new byte[0]);

            Assert.That(Priv.Call<string>(v, "expandUrl", server.BaseUrl + "s/abc"), Is.EqualTo(target));
            Assert.That(server.Requests.First(), Does.StartWith("HEAD "), "basta una HEAD, non serve scaricare la pagina");
        }

        [Test]
        public void ExpandUrl_SenzaRedirect_StessoIndirizzo() {
            server.Handler = _ => ("HTTP/1.1 200 OK\r\n", new byte[0]);
            Assert.That(Priv.Call<string>(v, "expandUrl", server.BaseUrl + "x"), Is.EqualTo(server.BaseUrl + "x"));
        }

        [Test]
        public void ExpandUrl_Errore_RestituisceLOriginale() {
            server.Dispose();
            Assert.That(Priv.Call<string>(v, "expandUrl", server.BaseUrl + "x"), Is.EqualTo(server.BaseUrl + "x"));
        }

        [Test]
        public void Importanti_SalvaERicaricaCoppieCortoLungo() {
            var sq = typeof(Viewer.Squeeze);
            var important = new Dictionary<string, Viewer.Squeeze> {
                ["282248#12"] = new Viewer.Squeeze("https://bit.ly/abc", "https://antenati.cultura.gov.it/ark:/12657/an_ua282248/LqaVpAR"),
            };
            Priv.Set(v, "important", important);
            try {
                Priv.Call(v, "saveImportant");
                Priv.Set(v, "important", new Dictionary<string, Viewer.Squeeze>());
                Priv.Call(v, "loadImportant");

                var back = (Dictionary<string, Viewer.Squeeze>)Priv.Get(v, "important");
                Assert.That(back["282248#12"]._short, Is.EqualTo("https://bit.ly/abc"));
                Assert.That(back["282248#12"]._long, Is.EqualTo("https://antenati.cultura.gov.it/ark:/12657/an_ua282248/LqaVpAR"));
            }
            finally { File.Delete("important_squized.json"); }
        }

        [Test]
        public void Importanti_MigrazioneDalVecchioFormato_EspandeGliIndirizzi() {
            string target = server.BaseUrl + "ark:/12657/an_ua282248/LqaVpAR";
            server.Handler = req => Path0(req).StartsWith("/s/")
                ? ($"HTTP/1.1 301 Moved Permanently\r\nLocation: {target}\r\n", new byte[0])
                : ("HTTP/1.1 200 OK\r\n", new byte[0]);
            File.WriteAllText("important.json", JsonConvert.SerializeObject(new Dictionary<string, string> { ["282248#12"] = server.BaseUrl + "s/abc" }));
            Priv.Set(v, "important", new Dictionary<string, Viewer.Squeeze>());
            try {
                Priv.Call(v, "loadImportant");
                var imp = (Dictionary<string, Viewer.Squeeze>)Priv.Get(v, "important");
                Assert.That(imp["282248#12"]._short, Is.EqualTo(server.BaseUrl + "s/abc"));
                Assert.That(imp["282248#12"]._long, Is.EqualTo(target));
            }
            finally { File.Delete("important.json"); }
        }
    }
}
