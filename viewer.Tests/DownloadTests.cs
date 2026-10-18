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
        string savedIiif, savedDam;

        [SetUp]
        public void SetUp() {
            Tree.Reset();
            server = new LocalHttpServer();
            savedIiif = Viewer.iiifBaseUrl;
            savedDam = ManifestLoader.damBaseUrl;
            Viewer.iiifBaseUrl = server.BaseUrl + "iiif/2/";
            ManifestLoader.damBaseUrl = server.BaseUrl + "containers/";

            v = Priv.NewViewer();
            Priv.Set(v, "currExploring", "");
        }

        [TearDown]
        public void TearDown() {
            Viewer.iiifBaseUrl = savedIiif;
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
            Priv.Set(v, "existingRequests", new HashSet<string>());
            Priv.Set(v, "failCount", new Dictionary<string, int>());
            Priv.Set(v, "Sdownload", new Semaphore(5, 20));
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
            Assert.That((HashSet<string>)Priv.Get(v, "existingRequests"), Is.Empty, "la richiesta in corso va tolta");
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
