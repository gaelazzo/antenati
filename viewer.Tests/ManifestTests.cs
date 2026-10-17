using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System.IO;

namespace viewer.Tests {

    [TestFixture]
    public class ManifestTests {

        [SetUp]
        public void SetUp() { Tree.Reset(); }

        /// <summary>Manifest IIIF v2 nella forma servita da dam-antenati (canvas @id = ark del registro + codice immagine)</summary>
        static JObject IiifManifest(params (string code, int? width)[] pages) {
            var canvases = new JArray();
            foreach (var p in pages) {
                var c = new JObject { ["@id"] = "https://antenati.cultura.gov.it/ark:/12657/an_ua282248/" + p.code };
                if (p.width.HasValue) c["width"] = p.width.Value;
                canvases.Add(c);
            }
            return new JObject {
                ["sequences"] = new JArray { new JObject { ["canvases"] = canvases } },
                ["metadata"] = new JArray {
                    new JObject { ["label"] = "Conservato da", ["value"] = "Archivio di Stato di Bari" },
                    new JObject { ["label"] = "Contesto archivistico", ["value"] = "Archivio di Stato di Bari > Stato civile napoleonico > Bari" },
                    new JObject { ["label"] = "Titolo", ["value"] = "1813" },
                    new JObject { ["label"] = "Tipologia", ["value"] = "Nati" },
                }
            };
        }

        [Test]
        public void FromJson_PagineNumerateDaUno() {
            var m = Manifest.fromJson(IiifManifest(("wOgD6pM", 2000), ("LqaVpAR", 1800), ("5xyVJYW", null)));

            Assert.That(m.mapPage[1], Is.EqualTo("wOgD6pM"));
            Assert.That(m.mapPage[3], Is.EqualTo("5xyVJYW"));
            Assert.That(m.pageDecode["LqaVpAR"], Is.EqualTo(2));
            Assert.That(m.mapPage.Count, Is.EqualTo(3));
        }

        [Test]
        public void FromJson_LarghezzaPagina_DefaultMille() {
            var m = Manifest.fromJson(IiifManifest(("a", 2000), ("b", null)));
            Assert.That(m.pageWidth["a"], Is.EqualTo(2000));
            Assert.That(m.pageWidth["b"], Is.EqualTo(1000));
        }

        [Test]
        public void FromJson_Metadati() {
            var m = Manifest.fromJson(IiifManifest(("a", 1)));

            Assert.That(m.idRegistro, Is.EqualTo("282248"));
            Assert.That(m.archivio, Is.EqualTo("Archivio di Stato di Bari"));
            Assert.That(m.registerName, Is.EqualTo("Stato civile napoleonico"));
            Assert.That(m.Comune, Is.EqualTo("Bari"));
            Assert.That(m.Anno, Is.EqualTo("1813"));
            Assert.That(m.tipologia, Is.EqualTo("Nati"));
        }

        [TestCase("Archivio di Stato di Bari > Stato civile italiano > Carbonara", "Stato civile italiano", "Carbonara")]
        [TestCase("Archivio di Stato di Napoli > Stato civile della restaurazione (quartieri di Napoli) > Arenella",
                  "Stato civile della restaurazione (quartieri di Napoli)", "Arenella")]
        [TestCase("Archivio > Fondo > Sottofondo > Comune", "Fondo", "Comune")]
        [TestCase("Archivio di Stato di Bari > Inventario", "Inventario", null)]
        [TestCase("Archivio di Stato di Bari", null, null)]
        [TestCase("Archivio>Fondo>Comune", "Fondo", "Comune")]
        [TestCase("  Archivio  >  Fondo  >  Comune  ", "Fondo", "Comune")]
        [TestCase("", null, null)]
        [TestCase(null, null, null)]
        public void ContestoArchivistico_FondoEComune(string contesto, string fondo, string comune) {
            Assert.That(Priv.Call(typeof(Manifest), "GetRegistroFromContestoArchivistico", contesto), Is.EqualTo(fondo));
            Assert.That(Priv.Call(typeof(Manifest), "GetComuneFromContestoArchivistico", contesto), Is.EqualTo(comune));
        }

        [Test]
        public void FromJson_ContestoCorto_NonBloccaIlRegistro() {
            // Prima un contesto con meno di 3 livelli lanciava IndexOutOfRange dentro LoadManifest
            var json = IiifManifest(("a", 1));
            json["metadata"][1]["value"] = "Archivio di Stato di Bari > Inventario";
            var m = Manifest.fromJson(json);
            Assert.That(m.registerName, Is.EqualTo("Inventario"));
            Assert.That(m.Comune, Is.Null);
            Assert.That(m.mapPage[1], Is.EqualTo("a"));
        }

        [Test]
        public void FromJson_SenzaMetadati_SoloPagine() {
            var json = IiifManifest(("a", 1));
            json.Remove("metadata");
            var m = Manifest.fromJson(json);
            Assert.That(m.mapPage[1], Is.EqualTo("a"));
            Assert.That(m.archivio, Is.Null);
            Assert.That(m.Comune, Is.Null);
        }

        [Test]
        public void FromJson_SenzaCanvas_ManifestVuoto() {
            var json = IiifManifest();
            json.Remove("sequences");
            var m = Manifest.fromJson(json);
            Assert.That(m.mapPage, Is.Empty);
            Assert.That(m.idRegistro, Is.Null);
        }

        [Test]
        public void Serializzazione_NonSalvaPageDecode() {
            var m = Manifest.fromJson(IiifManifest(("a", 1)));
            var json = JObject.Parse(JsonConvert.SerializeObject(m));
            Assert.That(json.ContainsKey("pageDecode"), Is.False);
            Assert.That(json.ContainsKey("mapPage"), Is.True);
        }

        [Test]
        public void LoadManifest_DallaCacheSuDisco_RicostruiscePageDecode() {
            string id = "test" + System.Guid.NewGuid().ToString("N").Substring(0, 6);
            var original = Manifest.fromJson(IiifManifest(("wOgD6pM", 2000), ("LqaVpAR", 1800)));
            Directory.CreateDirectory("manifest");
            File.WriteAllText(Manifest.manifestFileName(id), JsonConvert.SerializeObject(original));

            var m = Manifest.LoadManifest(id).Result;

            Assert.That(m.mapPage, Is.EqualTo(original.mapPage));
            Assert.That(m.pageDecode["LqaVpAR"], Is.EqualTo(2));
            Assert.That(m.Comune, Is.EqualTo("Bari"));
        }

        [Test]
        public void LoadManifest_SecondaChiamata_StessaIstanzaInMemoria() {
            string id = "test" + System.Guid.NewGuid().ToString("N").Substring(0, 6);
            Directory.CreateDirectory("manifest");
            File.WriteAllText(Manifest.manifestFileName(id), JsonConvert.SerializeObject(Manifest.fromJson(IiifManifest(("a", 1)))));

            var first = Manifest.LoadManifest(id).Result;
            File.Delete(Manifest.manifestFileName(id));
            var second = Manifest.LoadManifest(id).Result;

            Assert.That(second, Is.SameAs(first));
        }

        [Test]
        public void ManifestFileName_NellaCartellaManifest() {
            Assert.That(Manifest.manifestFileName("wOgD6pM"), Is.EqualTo(Path.Combine("manifest", "wOgD6pM.json")));
        }
    }
}
