using NUnit.Framework;

namespace viewer.Tests {

    /// <summary>
    /// Il registro legge il proprio indirizzo (Registro.href, ".../ark:/12657/an_ua{id}[/{codice pagina}]")
    /// anche nell'altro verso. Prima la lettura era scritta a mano in quattro punti: decodeUrl
    /// (parts[5].Substring(5), parts[6]), btnGotoRegister e deriveRegistroFromPage (regex an_ua) e
    /// il manifest (pieces[5], pieces[6] dell'@id di ogni pagina).
    /// </summary>
    [TestFixture]
    public class RegistroHrefTests {

        // @id reale di una pagina nel manifest w6k4eEe (Montecalvario 1817, Morti), letto il 20/10/2026
        const string Pagina = "https://antenati.cultura.gov.it/ark:/12657/an_ua17427393/LN2KqYO";

        [TestCase(Pagina, "17427393")]
        [TestCase("https://antenati.cultura.gov.it/ark:/12657/an_ua17427393", "17427393")]
        [TestCase("https://antenati.cultura.gov.it/ark:/12657/an_ua282226/", "282226")]          // redirect della ricerca
        [TestCase("https://antenati.cultura.gov.it/ark:/12657/an_ua17439642/wbOG7kv/", "17439642")]
        [TestCase("vedi https://antenati.cultura.gov.it/ark:/12657/an_ua244110/LPbBpOd/ qui", "244110")]
        public void IdDelRegistro(string url, string id) {
            Assert.That(Registro.idFromHref(url), Is.EqualTo(id));
        }

        [TestCase(Pagina, "LN2KqYO")]
        [TestCase("https://antenati.cultura.gov.it/ark:/12657/an_ua17439642/wbOG7kv/", "wbOG7kv")]
        [TestCase("https://antenati.cultura.gov.it/ark:/12657/an_ua244110/LPbBpOd?x=1", "LPbBpOd")]
        public void CodiceDellaPagina(string url, string code) {
            Assert.That(Registro.pageCodeFromHref(url), Is.EqualTo(code));
        }

        [TestCase("https://antenati.cultura.gov.it/ark:/12657/an_ua17427393")]
        [TestCase("https://antenati.cultura.gov.it/ark:/12657/an_ua282226/")]
        public void SenzaPagina_CodiceNull(string url) {
            Assert.That(Registro.pageCodeFromHref(url), Is.Null);
            Assert.That(Registro.idFromHref(url), Is.Not.Null, "il registro c'e' comunque");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("https://antenati.cultura.gov.it/ark:/12657/")]
        [TestCase("https://antenati.cultura.gov.it/search-registry/?serie=282216")]
        [TestCase("https://iiif-antenati.cultura.gov.it/iiif/2/LN2KqYO/full/full/0/default.jpg")]
        [TestCase("an_uaABC/xyz")]
        public void IndirizzoSenzaRegistro_Null(string url) {
            Assert.That(Registro.idFromHref(url), Is.Null);
            Assert.That(Registro.pageCodeFromHref(url), Is.Null);
        }

        [Test]
        public void AndataERitorno() {
            // senza costruttore: vorrebbe l'albero AnnoSerieKind caricato, qui basta l'id
            var r = (Registro)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Registro));
            r.idRegistro = "17427393";
            Assert.That(Registro.idFromHref(r.href), Is.EqualTo("17427393"));
            Assert.That(Registro.idFromHref(r.href + "/LN2KqYO"), Is.EqualTo(r.idRegistro));
            Assert.That(Registro.pageCodeFromHref(r.href + "/LN2KqYO"), Is.EqualTo("LN2KqYO"));
        }
    }
}
