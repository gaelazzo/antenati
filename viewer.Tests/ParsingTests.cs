using NUnit.Framework;
using System.Linq;

namespace viewer.Tests {

    /// <summary>
    /// Estrazione dei nodi dalle pagine di Antenati. Le fixture in Fixtures\ sono pagine reali
    /// salvate il 17/10/2026 (archivio 208 = Bari, fondo 281500, serie 282216 = Bari, anno 1813):
    /// se il sito cambia markup, aggiornarle con i test [Explicit] di OnlineTests.
    /// </summary>
    [TestFixture]
    public class ParsingTests {

        [SetUp]
        public void SetUp() { Tree.Reset(); }

        // ---------- Archivi ----------

        [Test]
        public void Archivi_DallaPaginaEsploraArchivi() {
            Archivio.parseArchivi(Sandbox.ReadFixture("archivi.html"));

            Assert.That(Archivio.archivi.Count, Is.GreaterThan(80));
            Assert.That(Archivio.archivioById["208"].description, Is.EqualTo("Archivio di Stato di Bari"));
            Assert.That(Archivio.archivi.All(a => !string.IsNullOrWhiteSpace(a.description)), Is.True);
            Assert.That(Archivio.archivi.All(a => a.idArchivio.All(char.IsDigit)), Is.True);
        }

        [Test]
        public void Archivi_EntitaHtmlDecodificate() {
            Archivio.parseArchivi(Sandbox.ReadFixture("archivi.html"));
            Assert.That(Archivio.archivi.Any(a => a.description.Contains("&#") || a.description.Contains("&amp;")), Is.False);
        }

        [Test]
        public void Archivi_Sintetico() {
            string html = @"x.setContent(""<div>   <h3>Archivio di Stato dell&#039;Aquila</h3>
                <a href='https://antenati.cultura.gov.it/archivio/aq/'>Scopri l&#039;archivio</a>
                <a href='/archive/?archivio=236&#038;lang=it'>Esplora i fondi</a>
                <a href='/search-registry/?archivio=236'>Cerca nei registri</a></div>"")";
            Archivio.parseArchivi(html);

            Assert.That(Archivio.archivi.Single().idArchivio, Is.EqualTo("236"));
            Assert.That(Archivio.archivi.Single().description, Is.EqualTo("Archivio di Stato dell'Aquila"));
        }

        [Test]
        public void Archivi_BloccoSenzaCodice_Ignorato() {
            Archivio.parseArchivi(@"x.setContent(""<div><h3>Senza link</h3><a href='/altro'>x</a></div>"")");
            Assert.That(Archivio.archivi, Is.Empty);
        }

        // ---------- Fondi ----------

        [Test]
        public void Fondi_DallaPaginaArchivio() {
            Tree.AddArchivio();
            var res = Fondo.parseFondi("208", Sandbox.ReadFixture("archivio_208.html"));

            Assert.That(res.Select(n => n.key), Is.EqualTo(new[] { "242623", "281500", "3804025", "20352649" }));
            Assert.That(res.Select(n => n.description),
                Is.EqualTo(new[] { "Stato civile della restaurazione", "Stato civile napoleonico", "Stato civile italiano", "Inventario" }));
            Assert.That(Fondo.fondoById["281500"].parentElement, Is.SameAs(Archivio.archivioById["208"]));
            Assert.That(res.All(n => n.tipo == tipoNodo.Fondo), Is.True);
        }

        [Test]
        public void Fondi_ApiciSingoliEAndateACapo() {
            Tree.AddArchivio();
            string html = @"<div id=""archive_property_select"" hidden>
                <select><option value='111'>
                        Stato civile italiano
                    </option></select></div>";
            var res = Fondo.parseFondi("208", html);
            Assert.That(res.Single().description, Is.EqualTo("Stato civile italiano"));
        }

        [Test]
        public void Fondi_PaginaSenzaSelect_Vuoto() {
            Tree.AddArchivio();
            Assert.That(Fondo.parseFondi("208", "<html><body>niente</body></html>"), Is.Empty);
        }

        // ---------- Serie ----------

        [Test]
        public void Serie_DelFondoDallaPaginaArchivio() {
            Tree.AddArchivio();
            Tree.AddFondo();
            var res = Serie.parseSerie("281500", Sandbox.ReadFixture("archivio_208.html"));

            Assert.That(res.Count, Is.EqualTo(54));
            var bari = Serie.serieById["282216"];
            Assert.That(bari.description, Is.EqualTo("Bari"));
            Assert.That(bari.idFondo, Is.EqualTo("281500"));
            Assert.That(bari.idArchivio, Is.EqualTo("208"));
        }

        [Test]
        public void Serie_RiletturaNonDuplica() {
            Tree.AddArchivio();
            Tree.AddFondo();
            string html = Sandbox.ReadFixture("archivio_208.html");
            var first = Serie.parseSerie("281500", html);
            var second = Serie.parseSerie("281500", html);

            Assert.That(Serie.series.Count, Is.EqualTo(first.Count));
            Assert.That(second[0], Is.SameAs(first[0]), "alla rilettura restituisce le istanze gia' note");
        }

        [Test]
        public void Serie_FondoSbagliato_Vuoto() {
            Tree.AddArchivio();
            Tree.AddFondo("208", "999999", "inesistente");
            Assert.That(Serie.parseSerie("999999", Sandbox.ReadFixture("archivio_208.html")), Is.Empty);
        }

        // ---------- Anni ----------

        [Test]
        public void Anni_DallaPaginaSerie() {
            Tree.Bari();
            var res = AnnoSerie.parseAnni("282216", Sandbox.ReadFixture("serie_282216.html"));

            var anni = res.Cast<AnnoSerie>().Select(a => a.anno).ToList();
            Assert.That(anni.Count, Is.EqualTo(7));
            Assert.That(anni, Is.Unique);
            Assert.That(anni, Does.Contain("1813"));
            Assert.That(Serie.serieById["282216"].myAnnoSeries.Count, Is.EqualTo(7));
            Assert.That(AnnoSerie.annoSerieById["282216/1813"].parentElement, Is.SameAs(Serie.serieById["282216"]));
        }

        [Test]
        public void Anni_FacetDuplicati_UnaVoltaSola() {
            Tree.Bari();
            string html = @"<a href=""#"" data-facet-term=""anni_is:1820"">1820</a>
                            <a class=""x"" data-facet-term=""anni_is:1820"">1820</a>
                            <a data-facet-term=""anni_is:1821"" title="""">1821</a>";
            var res = AnnoSerie.parseAnni("282216", html);
            Assert.That(res.Select(n => n.key), Is.EqualTo(new[] { "282216/1820", "282216/1821" }));
        }

        // ---------- Tipologie ----------

        [Test]
        public void Tipologie_DallaPaginaSerieAnno() {
            Tree.Bari();
            Tree.AddAnno();
            var res = AnnoSerieKind.parseKinds("282216/1813", Sandbox.ReadFixture("serie_282216_1813.html"));

            Assert.That(res.Select(n => n.description),
                Is.EqualTo(new[] { "Nati", "Diversi", "Matrimoni", "Morti", "Morti, indice" }));
        }

        [Test]
        public void Tipologie_ConVirgolaTraQuot() {
            Tree.Bari();
            Tree.AddAnno();
            string html = @"<a data-facet-term=""tipologia_ss:&quot;Nati, indice&quot;""><span>Nati, indice</span></a>
                            <a data-facet-term=""tipologia_ss:Nati""><span>Nati</span></a>";
            var res = AnnoSerieKind.parseKinds("282216/1813", html);
            Assert.That(res.Select(n => n.key), Is.EqualTo(new[] { "282216/1813/Nati, indice", "282216/1813/Nati" }));
        }

        [Test]
        public void Tipologie_GiaPresenti_Saltate() {
            Tree.Bari();
            Tree.AddAnno();
            string html = Sandbox.ReadFixture("serie_282216_1813.html");
            AnnoSerieKind.parseKinds("282216/1813", html);
            var again = AnnoSerieKind.parseKinds("282216/1813", html);

            Assert.That(again, Is.Empty);
            Assert.That(AnnoSerieKind.annoSeriesKind.Count, Is.EqualTo(5));
        }

        // ---------- Registri ----------

        [Test]
        public void Registri_PiuParti_UsanoLaSegnatura() {
            Tree.Bari();
            Tree.AddAnno();
            Tree.AddKind();
            var res = Registro.parseRegistri("1813", "282216", "Nati", Sandbox.ReadFixture("serie_282216_1813_Nati.html"));

            Assert.That(res.Select(n => n.key), Is.EqualTo(new[] { "282248", "282249" }));
            Assert.That(res.Select(n => n.description), Is.EqualTo(new[] { "Parte 1", "Parte 2" }));
            Assert.That(Registro.registroById["282248"].parentElement, Is.SameAs(AnnoSerieKind.annoSerieKindById["282216/1813/Nati"]));
        }

        [Test]
        public void Registri_UnicoSenzaSegnatura_DiventaUnico() {
            Tree.Bari();
            Tree.AddAnno();
            Tree.AddKind();
            string html = @"<li class=""search-item"" data-id=""274175""><div>
                <h3 class=""text-primary""><a href=""/ark:/12657/an_ua274175"" class="""" title="""">
                    Registro: 1813    </a></h3>
                <p><strong>Nati</strong></p></div></li>";
            var res = Registro.parseRegistri("1813", "282216", "Nati", html);
            Assert.That(res.Single().key, Is.EqualTo("274175"));
            Assert.That(res.Single().description, Is.EqualTo("unico"));
        }

        [Test]
        public void Registri_PiuRisultatiSenzaSegnatura_UsanoIlTitolo() {
            Tree.Bari();
            Tree.AddAnno();
            Tree.AddKind();
            string item = @"<li class=""search-item"" data-id=""{0}""><div>
                <h3><a href=""/ark:/12657/an_ua{0}"">Registro: {1}</a></h3></div></li>";
            string html = string.Format(item, "1", "1813") + string.Format(item, "2", "1813/1814");
            var res = Registro.parseRegistri("1813", "282216", "Nati", html);
            Assert.That(res.Select(n => n.description), Is.EqualTo(new[] { "1813", "1813/1814" }));
        }

        // ---------- Varianti a risultato unico (redirect alla pagina del registro) ----------

        [Test]
        public void RisultatoUnico_RegistroDallUrlDelRedirect() {
            Tree.Bari();
            Tree.AddAnno();
            Tree.AddKind();
            var res = Registro.deriveRegistroFromPage("1813", "282216", "Nati",
                "https://antenati.cultura.gov.it/ark:/12657/an_ua282248/");
            Assert.That(res.Single().key, Is.EqualTo("282248"));
            Assert.That(res.Single().description, Is.EqualTo("unico"));
        }

        [Test]
        public void RisultatoUnico_UrlSenzaCodice_Null() {
            Assert.That(Registro.deriveRegistroFromPage("1813", "282216", "Nati", "https://antenati.cultura.gov.it/"), Is.Null);
        }

        // Il codice del registro sta solo nell'url del redirect: la pagina non contiene "an_ua".
        // Prima della correzione in AnnoSerieKind.deriveKindFromRegistroPage il registro non veniva creato.
        [Test]
        public void RisultatoUnico_AnnoTipologiaERegistroDallaPaginaRegistro() {
            Tree.Bari();
            string url = "https://antenati.cultura.gov.it/ark:/12657/an_ua282248/";
            var res = AnnoSerie.deriveAnnoSerieFromRegistroPage("282216", Sandbox.ReadFixture("registro_282248.html"), url);

            Assert.That(res.Select(n => n.tipo),
                Is.EqualTo(new[] { tipoNodo.AnnoSerie, tipoNodo.AnnoSerieKind, tipoNodo.Registro }));
            Assert.That(res[0].key, Is.EqualTo("282216/1813"));
            Assert.That(res[1].key, Is.EqualTo("282216/1813/Nati"));
            Assert.That(res[2].key, Is.EqualTo("282248"));
        }

        [Test]
        public void RisultatoUnico_AnnoGiaNoto_RiusaIstanza() {
            Tree.Bari();
            var anno = Tree.AddAnno();
            var res = AnnoSerie.deriveAnnoSerieFromRegistroPage("282216", Sandbox.ReadFixture("registro_282248.html"), "u");
            Assert.That(res[0], Is.SameAs(anno));
            Assert.That(AnnoSerie.annoSeries.Count, Is.EqualTo(1));
        }

        [Test]
        public void RisultatoUnico_AnnoComeIntervallo() {
            Tree.Bari();
            string html = @"<h2>Registro: 1809-1810</h2>
                <p><a href=""/search-registry/?tipologia=Morti&serie=282216""><strong>Morti</strong></a></p>";
            var res = AnnoSerie.deriveAnnoSerieFromRegistroPage("282216", html, "https://antenati.cultura.gov.it/ark:/12657/an_ua777/");

            Assert.That(res.Select(n => n.key), Is.EqualTo(new[] { "282216/1809-1810", "282216/1809-1810/Morti", "777" }));
        }

        [Test]
        public void RisultatoUnico_PaginaSenzaAnno_Vuoto() {
            Tree.Bari();
            Assert.That(AnnoSerie.deriveAnnoSerieFromRegistroPage("282216", "<h2>Altro</h2>", "u"), Is.Empty);
        }

        // ---------- Id del manifest ----------

        [Test]
        public void ManifestId_DalWindowsIdDellaPaginaRegistro() {
            Assert.That(ManifestIdExtractor.ExtractManifestIdFromHtml(Sandbox.ReadFixture("registro_282248.html")),
                Is.EqualTo("wOgD6pM"));
        }

        [Test]
        public void ManifestId_Assente_Null() {
            Assert.That(ManifestIdExtractor.ExtractManifestIdFromHtml("<script>let altro = 'x';</script>"), Is.Null);
        }
    }
}
