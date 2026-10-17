using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;

namespace viewer.Tests {

    /// <summary>
    /// Verifica contro il sito vero: servono a capire se Antenati ha cambiato markup.
    /// [Explicit] = non partono mai da soli (poche GET, ma il server va trattato con riguardo).
    ///   dotnet test viewer.Tests --no-build --filter TestCategory=Online
    /// Con REFRESH_FIXTURES=1 riscrivono anche le pagine in Fixtures\ del sorgente.
    /// </summary>
    [TestFixture, Explicit("Accede ad antenati.cultura.gov.it"), Category("Online")]
    public class OnlineTests {

        [SetUp]
        public void SetUp() { Tree.Reset(); }

        static string Get(string url, string fixtureName) {
            string html = PageLoader.getPage(url);
            if (System.Environment.GetEnvironmentVariable("REFRESH_FIXTURES") == "1") {
                // TestDirectory = viewer.Tests\bin\<conf>\net48 -> risale al sorgente
                string src = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, @"..\..\..\Fixtures", fixtureName));
                File.WriteAllText(src, html, new UTF8Encoding(false));
            }
            return html;
        }

        [Test]
        public void Archivi() {
            Archivio.parseArchivi(Get("https://antenati.cultura.gov.it/esplora-gli-archivi/", "archivi.html"));
            Assert.That(Archivio.archivioById.ContainsKey("208"), Is.True);
        }

        [Test]
        public void FondiESerie() {
            Tree.AddArchivio();
            string html = Get("https://antenati.cultura.gov.it/archive/?archivio=208", "archivio_208.html");
            Assert.That(Fondo.parseFondi("208", html).Select(f => f.key), Does.Contain("281500"));
            Assert.That(Serie.parseSerie("281500", html).Select(s => s.key), Does.Contain("282216"));
        }

        [Test]
        public void AnniTipologieRegistri() {
            Tree.Bari();
            Assert.That(AnnoSerie.parseAnni("282216",
                Get("https://antenati.cultura.gov.it/search-registry/?serie=282216", "serie_282216.html")).Select(a => a.key),
                Does.Contain("282216/1813"));
            Assert.That(AnnoSerieKind.parseKinds("282216/1813",
                Get("https://antenati.cultura.gov.it/search-registry/?serie=282216&s_facet_query=anni_is%3A1813", "serie_282216_1813.html")).Select(k => k.description),
                Does.Contain("Nati"));
            Assert.That(Registro.parseRegistri("1813", "282216", "Nati",
                Get("https://antenati.cultura.gov.it/search-registry/?serie=282216&s_facet_query=tipologia_ss%3ANati%252Canni_is%3A1813", "serie_282216_1813_Nati.html")).Select(r => r.key),
                Does.Contain("282248"));
        }

        [Test]
        public void PaginaRegistro_ManifestId() {
            string html = Get("https://antenati.cultura.gov.it/ark:/12657/an_ua282248", "registro_282248.html");
            Assert.That(ManifestIdExtractor.ExtractManifestIdFromHtml(html), Is.EqualTo("wOgD6pM"));
        }

        [Test]
        public void ManifestIiif_ConvertibileDalCodice() {
            // Stessi header di ManifestLoader, ma senza le MessageBox in caso di errore
            PageLoader.getResponseMessage("https://antenati.cultura.gov.it/")?.Dispose();
            var headers = new Dictionary<string, string> {
                ["Accept"] = "*/*", ["Accept-Encoding"] = "gzip, deflate", ["Origin"] = "https://antenati.cultura.gov.it",
                ["Referer"] = "https://antenati.cultura.gov.it", ["Sec-Fetch-Site"] = "same-site", ["Sec-Fetch-Mode"] = "cors",
            };
            var cookies = new Dictionary<string, string> { ["PHPSESSID"] = PageLoader.getCookie("PHPSESSID") };
            using (var r = PageLoader.getResponseMessage("https://dam-antenati.cultura.gov.it/antenati/containers/wOgD6pM/manifest",
                                                        HttpCompletionOption.ResponseContentRead, headers, cookies)) {
                Assert.That(r, Is.Not.Null);
                Assert.That((int)r.StatusCode, Is.EqualTo(200), "dam-antenati ha risposto " + r.StatusCode);
                var json = JObject.Parse(r.Content.ReadAsStringAsync().Result);
                foreach (var meta in json["metadata"])
                    TestContext.Out.WriteLine($"metadata: {meta["label"]} = {meta["value"]}");
                var m = Manifest.fromJson(json);
                Assert.That(m.idRegistro, Is.EqualTo("282248"));
                Assert.That(m.mapPage.Count, Is.GreaterThan(0));
                Assert.That(m.Comune, Is.EqualTo("Bari"));
            }
        }
    }
}
