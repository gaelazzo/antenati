using NUnit.Framework;
using System;
using System.Reflection;

namespace viewer.Tests {

    /// <summary>
    /// Gli indirizzi del sito che compone ogni tipo di pagina, scritti per intero: la base comune
    /// (AntenatiSite) non deve cambiarne nemmeno un carattere. Prima ogni classe aveva la sua copia
    /// di "https://antenati.cultura.gov.it" (Fondo due volte la stessa).
    /// </summary>
    [TestFixture]
    public class SiteUrlTests {
        const string S = "https://antenati.cultura.gov.it/";

        [SetUp]
        public void SetUp() {
            Tree.Reset();
            Tree.Bari();
            Tree.AddAnno();
            Tree.AddKind();
            Tree.AddRegistro();
        }

        static string Const(Type t, string name) =>
            (string)t.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static).GetValue(null);

        static string Static(Type t, string method, params object[] args) => Priv.Call<string>(t, method, args);

        [Test]
        public void Archivio() {
            Assert.That(viewer.Archivio.archivioById["208"].href, Is.EqualTo(S + "archive/?archivio=208"));
            Assert.That(Const(typeof(Archivio), "ArchivioIndexUrl"), Is.EqualTo(S + "esplora-gli-archivi/"));
        }

        [Test]
        public void Fondo() {
            Assert.That(viewer.Fondo.fondoById["281500"].href, Is.EqualTo(S + "archive/?archivio=208"));
            Assert.That(Static(typeof(Fondo), "getIndexUrl", "208"), Is.EqualTo(S + "archive/?archivio=208"));
        }

        [Test]
        public void Serie() {
            Assert.That(viewer.Serie.serieById["282216"].href, Is.EqualTo(S + "search-registry/?serie=282216"));
            Assert.That(Static(typeof(Serie), "getIndexUrl", "208"), Is.EqualTo(S + "archive/?archivio=208"));
            Assert.That(viewer.Serie.getIndexUrl2("282216"), Is.EqualTo(S + "search-registry/?localita=Bari"));
        }

        [Test]
        public void AnnoSerie() {
            Assert.That(viewer.AnnoSerie.annoSerieById[viewer.AnnoSerie.annoSeries[0].key].href,
                Is.EqualTo(S + "search-registry/?serie=282216&s_facet_query=anni_is%3A1813"));
            Assert.That(Static(typeof(AnnoSerie), "getIndexUrl", "282216"), Is.EqualTo(S + "search-registry/?serie=282216"));
            Assert.That(viewer.AnnoSerie.getIndexUrl2("282216", "1813"),
                Is.EqualTo(S + "search-registry/?localita=Bari&s_facet_query=anni_is%3A1813"));
        }

        [Test]
        public void AnnoSerieKind() {
            Assert.That(viewer.AnnoSerieKind.annoSeriesKind[0].href,
                Is.EqualTo(S + "search-registry/?serie=282216&s_facet_query=tipologia_ss%3ANati%252Canni_is%3A1813"));
            Assert.That(Static(typeof(AnnoSerieKind), "getIndexUrl", "282216", "1813"),
                Is.EqualTo(S + "search-registry/?serie=282216&s_facet_query=anni_is%3A1813"));
        }

        [Test]
        public void Registro() {
            Assert.That(viewer.Registro.registroById["282248"].href, Is.EqualTo(S + "ark:/12657/an_ua282248"));
            Assert.That(Static(typeof(Registro), "getIndexUrl", "282216", "1813", "Nati"),
                Is.EqualTo(S + "search-registry/?serie=282216&s_facet_query=tipologia_ss%3ANati%252Canni_is%3A1813"));
            // "%252anni" senza la C: e' cosi' da sempre, qui si fissa com'e'
            Assert.That(viewer.Registro.getIndexUrl2("282216", "1813", "Nati"),
                Is.EqualTo(S + "search-registry/?localita=Bari&s_facet_query=tipologia_ss%3ANati%252anni_is%3A1813"));
        }

        [Test]
        public void HomeEProvenienza() {
            Assert.That(PageLoader.warmUpUrl, Is.EqualTo(S));
            Assert.That(new ServerProbe.Options().SiteUrl, Is.EqualTo(S));
            Assert.That(Const(typeof(Viewer), "addrPrefix"), Is.EqualTo(S));
            Assert.That(PageLoader.siteHeaders()["Referer"], Is.EqualTo("https://antenati.cultura.gov.it"));
            Assert.That(PageLoader.siteHeaders()["Origin"], Is.EqualTo("https://antenati.cultura.gov.it"));
        }
    }
}
