using NUnit.Framework;

namespace viewer.Tests {

    /// <summary>
    /// Il server IIIF delle immagini (API Image 2): prima gli indirizzi si componevano in Viewer
    /// (getImageUrlByCode), Manifest (info.json), ServerProbe (info.json, tasselli, pagina intera),
    /// nel filtro della WebView e si leggevano a mano in decodeUrl (parts[5]).
    /// </summary>
    [TestFixture]
    public class IiifServerTests {

        static readonly IiifServer Antenati = new IiifServer("https://iiif-antenati.cultura.gov.it/iiif/2/");

        [Test]
        public void ServerInUso_Antenati() {
            Assert.That(IiifServer.current.imageUrl("wOgD6pM", 0),
                Is.EqualTo("https://iiif-antenati.cultura.gov.it/iiif/2/wOgD6pM/full/max/0/default.jpg"));
        }

        [Test]
        public void PaginaIntera_LarghezzaFissa_OMax() {
            Assert.That(Antenati.imageUrl("wOgD6pM", 1600),
                Is.EqualTo("https://iiif-antenati.cultura.gov.it/iiif/2/wOgD6pM/full/1600,/0/default.jpg"));
            Assert.That(Antenati.imageUrl("wOgD6pM", 0),
                Is.EqualTo("https://iiif-antenati.cultura.gov.it/iiif/2/wOgD6pM/full/max/0/default.jpg"));
        }

        [Test]
        public void InfoJson() {
            Assert.That(Antenati.infoUrl("LPgEKzY"), Is.EqualTo("https://iiif-antenati.cultura.gov.it/iiif/2/LPgEKzY/info.json"));
        }

        [Test]
        public void Tassello() {
            // richiesta reale del visualizzatore del sito
            Assert.That(Antenati.tileUrl("5VrBEzV", "0,2048,1024,518/1024,"),
                Is.EqualTo("https://iiif-antenati.cultura.gov.it/iiif/2/5VrBEzV/0,2048,1024,518/1024,/0/default.jpg"));
        }

        [Test]
        public void Tasselli_ComeIlVisualizzatoreDelSito() {
            // l'esempio reale: LPgEKzY, 3789x2784, tasselli 1024 a scala 2
            var t = IiifServer.tileRegions(3789, 2784, 1024, 2);
            Assert.That(t, Is.EqualTo(new[] {
                "0,0,2048,2048/1024,", "2048,0,1741,2048/871,", "0,2048,2048,736/1024,", "2048,2048,1741,736/871," }));
        }

        [Test]
        public void BaseSenzaBarraFinale_StessiIndirizzi() {
            var s = new IiifServer("http://localhost:1234/iiif/2");
            Assert.That(s.infoUrl("X"), Is.EqualTo("http://localhost:1234/iiif/2/X/info.json"));
        }

        [Test]
        public void FiltroWebView_TuttoIlServer() {
            Assert.That(Antenati.requestFilter, Is.EqualTo("https://iiif-antenati.cultura.gov.it/*"));
        }

        [TestCase("https://iiif-antenati.cultura.gov.it/iiif/2/wOxV6Vr/full/full/0/default.jpg", "wOxV6Vr")]
        [TestCase("https://iiif-antenati.cultura.gov.it/iiif/2/LPgEKzY/info.json", "LPgEKzY")]
        [TestCase("https://iiif-antenati.cultura.gov.it/iiif/2/5VrBEzV/0,2048,1024,518/1024,/0/default.jpg", "5VrBEzV")]
        [TestCase("https://iiif-antenati.cultura.gov.it/iiif/2/wOxV6Vr", "wOxV6Vr")]
        public void CodiceDaIndirizzoImmagine(string url, string code) {
            Assert.That(Antenati.codeFromImageUrl(url), Is.EqualTo(code));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("https://iiif-antenati.cultura.gov.it/iiif/2/")]
        [TestCase("https://antenati.cultura.gov.it/ark:/12657/an_ua17427393/LN2KqYO")]
        [TestCase("https://altro.server/iiif/2/wOxV6Vr/full/max/0/default.jpg")]
        public void IndirizzoNonDiQuestoServer_Null(string url) {
            Assert.That(Antenati.codeFromImageUrl(url), Is.Null);
        }

        [Test]
        public void IndirizziComposti_SiRileggono() {
            Assert.That(Antenati.codeFromImageUrl(Antenati.imageUrl("abc", 900)), Is.EqualTo("abc"));
            Assert.That(Antenati.codeFromImageUrl(Antenati.infoUrl("abc")), Is.EqualTo("abc"));
        }

        [Test]
        public void InfoJson_Letto() {
            // info.json reale (ridotto) di LPgEKzY
            var i = IiifServer.parseInfo(@"{""@context"":""http://iiif.io/api/image/2/context.json"",""width"":3789,""height"":2784,
                ""tiles"":[{""width"":1024,""height"":1024,""scaleFactors"":[1,2,4,8,16]}]}");
            Assert.That(i.width, Is.EqualTo(3789));
            Assert.That(i.height, Is.EqualTo(2784));
            Assert.That(i.tileWidth, Is.EqualTo(1024));
        }

        [Test]
        public void InfoJson_SenzaTasselli_1024() {
            var i = IiifServer.parseInfo(@"{""width"":100,""height"":50}");
            Assert.That(i.tileWidth, Is.EqualTo(1024));
        }
    }
}
