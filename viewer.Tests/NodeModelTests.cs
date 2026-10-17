using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System.IO;
using System.Linq;

namespace viewer.Tests {

    /// <summary>Chiavi, gerarchia e indirizzi dei sei livelli dell'albero</summary>
    [TestFixture]
    public class NodeModelTests {

        [SetUp]
        public void SetUp() { Tree.Reset(); }

        [Test]
        public void Chiavi_ETipi() {
            Tree.Bari();
            var anno = Tree.AddAnno();
            var kind = Tree.AddKind();
            var reg = Tree.AddRegistro();

            Assert.That(Archivio.archivioById["208"].key, Is.EqualTo("208"));
            Assert.That(Fondo.fondoById["281500"].key, Is.EqualTo("281500"));
            Assert.That(Serie.serieById["282216"].key, Is.EqualTo("282216"));
            Assert.That(anno.key, Is.EqualTo("282216/1813"));
            Assert.That(kind.key, Is.EqualTo("282216/1813/Nati"));
            Assert.That(reg.key, Is.EqualTo("282248"));
            Assert.That(new IArchiveNode[] { Archivio.archivioById["208"], Fondo.fondoById["281500"], Serie.serieById["282216"], anno, kind, reg }
                .Select(n => n.tipo),
                Is.EqualTo(new[] { tipoNodo.Archivio, tipoNodo.Fondo, tipoNodo.Serie, tipoNodo.AnnoSerie, tipoNodo.AnnoSerieKind, tipoNodo.Registro }));
        }

        [Test]
        public void Genitori_RisalgonoFinoAllArchivio() {
            Tree.Bari();
            Tree.AddAnno();
            Tree.AddKind();
            IArchiveNode n = Tree.AddRegistro();

            var chain = new System.Collections.Generic.List<string>();
            while (n != null) { chain.Add(n.key); n = n.parentElement; }

            Assert.That(chain, Is.EqualTo(new[] { "282248", "282216/1813/Nati", "282216/1813", "282216", "281500", "208" }));
        }

        [Test]
        public void IdEreditatiDaiGenitori() {
            Tree.Bari();
            var anno = Tree.AddAnno();
            var kind = Tree.AddKind();
            var reg = Tree.AddRegistro();

            Assert.That(new[] { anno.idArchivio, kind.idArchivio, reg.idArchivio }, Is.All.EqualTo("208"));
            Assert.That(new[] { anno.idFondo, kind.idFondo, reg.idFondo }, Is.All.EqualTo("281500"));
        }

        [Test]
        public void AnnoSerie_SiRegistraNellaSerie() {
            var serie = Tree.Bari();
            var anno = Tree.AddAnno();
            Assert.That(serie.myAnnoSeries, Does.Contain(anno));
        }

        [Test]
        public void Indirizzi() {
            Tree.Bari();
            var anno = Tree.AddAnno("282216", "1809-1810");
            var kind = Tree.AddKind("282216", "1809-1810", "Nati");
            var reg = Tree.AddRegistro("282216", "1809-1810", "Nati", "282248");

            Assert.That(Archivio.archivioById["208"].href, Is.EqualTo("https://antenati.cultura.gov.it/archive/?archivio=208"));
            Assert.That(Serie.serieById["282216"].href, Is.EqualTo("https://antenati.cultura.gov.it/search-registry/?serie=282216"));
            Assert.That(anno.href, Is.EqualTo("https://antenati.cultura.gov.it/search-registry/?serie=282216&s_facet_query=anni_is%3A1809"),
                "per gli intervalli si cerca il primo anno");
            Assert.That(kind.href, Is.EqualTo("https://antenati.cultura.gov.it/search-registry/?serie=282216&s_facet_query=tipologia_ss%3ANati%252Canni_is%3A1809"));
            Assert.That(reg.href, Is.EqualTo("https://antenati.cultura.gov.it/ark:/12657/an_ua282248"));
        }

        [Test]
        public void Indirizzo_TipologiaConVirgola_TraVirgolette() {
            Tree.Bari();
            Tree.AddAnno();
            var kind = Tree.AddKind("282216", "1813", "Morti, indice");
            Assert.That(kind.href, Does.Contain("tipologia_ss%3A%22Morti%2C%20indice%22%252C"));
        }

        [Test]
        public void IndirizziAlternativi_PerLocalita() {
            var serie = Tree.Bari();
            Assert.That(Serie.getIndexUrl2("282216"), Is.EqualTo("https://antenati.cultura.gov.it/search-registry/?localita=Bari"),
                "senza localita usa la descrizione della serie");

            serie.localita = "San Nicandro Garganico";
            Assert.That(Serie.getIndexUrl2("282216"), Is.EqualTo("https://antenati.cultura.gov.it/search-registry/?localita=San%20Nicandro%20Garganico"));
            Assert.That(AnnoSerie.getIndexUrl2("282216", "1820-1821"), Does.EndWith("localita=San%20Nicandro%20Garganico&s_facet_query=anni_is%3A1820"));
            Assert.That(Registro.getIndexUrl2("282216", "1820", "Nati"), Does.Contain("localita=San%20Nicandro%20Garganico&s_facet_query=tipologia_ss%3ANati"));
        }

        [Test]
        public void ObjectKey() {
            Assert.That(AnnoSerie.objectKey("1", "1813"), Is.EqualTo("1/1813"));
            Assert.That(AnnoSerieKind.objectKey("1", "1813", "Nati"), Is.EqualTo("1/1813/Nati"));
            Assert.That(Registro.objectKey("1", "1813", "Nati"), Is.EqualTo(AnnoSerieKind.objectKey("1", "1813", "Nati")));
        }
    }

    /// <summary>Salvataggio e ricaricamento delle cache JSON di ogni livello</summary>
    [TestFixture]
    public class PersistenceTests {
        string dir;

        [SetUp]
        public void SetUp() {
            Tree.Reset();
            dir = Sandbox.NewDir();
        }

        string P(string name) => Path.Combine(dir, name);

        [Test]
        public void AlberoCompleto_SalvaERicarica() {
            Tree.Bari();
            Tree.AddAnno();
            Tree.AddKind();
            var reg = Tree.AddRegistro();
            reg.manifestId = "wOgD6pM";
            reg.lastPageViewed = "LqaVpAR";
            reg.idRegistroIndice = "999";
            reg.nPaginaIndice = 12;

            Archivio.Save(P("a.json"));
            Fondo.Save(P("f.json"));
            Serie.Save(P("s.json"));
            AnnoSerie.Save(P("as.json"));
            AnnoSerieKind.Save(P("ask.json"));
            Registro.Save(P("r.json"));

            Tree.Reset();
            Archivio.Load(P("a.json"));
            Fondo.Load(P("f.json"));
            Serie.Load(P("s.json"));
            AnnoSerie.Load(P("as.json"));
            AnnoSerieKind.Load(P("ask.json"));
            Registro.Load(P("r.json"));

            var r = Registro.registroById["282248"];
            Assert.That(r.manifestId, Is.EqualTo("wOgD6pM"));
            Assert.That(r.lastPageViewed, Is.EqualTo("LqaVpAR"));
            Assert.That(r.idRegistroIndice, Is.EqualTo("999"));
            Assert.That(r.nPaginaIndice, Is.EqualTo(12));
            Assert.That(r.description, Is.EqualTo("Parte 1"));
            Assert.That(r.parentElement.parentElement.parentElement.parentElement.parentElement.key, Is.EqualTo("208"));
            Assert.That(Archivio.archivioById["208"].description, Is.EqualTo("Archivio di Stato di Bari"));
        }

        [Test]
        public void CampiCalcolati_NonFinisconoNelJson() {
            Tree.Bari();
            Tree.AddAnno();
            Tree.AddKind();
            Tree.AddRegistro();
            Registro.Save(P("r.json"));
            AnnoSerie.Save(P("as.json"));

            var r = (JObject)JArray.Parse(File.ReadAllText(P("r.json")))[0];
            Assert.That(r.Properties().Select(p => p.Name),
                Is.EquivalentTo(new[] { "description", "idSerie", "kind", "anno", "manifestId", "idRegistro", "idRegistroIndice", "nPaginaIndice", "lastPageViewed" }));
            var a = (JObject)JArray.Parse(File.ReadAllText(P("as.json")))[0];
            Assert.That(a.Properties().Select(p => p.Name), Is.EquivalentTo(new[] { "idSerie", "anno" }));
        }

        [Test]
        public void Load_ScartaIDuplicati() {
            File.WriteAllText(P("f.json"), @"[{""idArchivio"":""208"",""idFondo"":""1"",""description"":""a""},
                                              {""idArchivio"":""208"",""idFondo"":""1"",""description"":""b""}]");
            File.WriteAllText(P("s.json"), @"[{""idFondo"":""1"",""idSerie"":""9"",""description"":""x""},
                                              {""idFondo"":""1"",""idSerie"":""9"",""description"":""y""}]");
            File.WriteAllText(P("as.json"), @"[{""idSerie"":""9"",""anno"":""1813""},{""idSerie"":""9"",""anno"":""1813""}]");

            Fondo.Load(P("f.json"));
            Serie.Load(P("s.json"));
            AnnoSerie.Load(P("as.json"));

            Assert.That(Fondo.fondi.Count, Is.EqualTo(1));
            Assert.That(Fondo.fondoById["1"].description, Is.EqualTo("a"), "vince il primo");
            Assert.That(Serie.series.Count, Is.EqualTo(1));
            Assert.That(AnnoSerie.annoSeries.Count, Is.EqualTo(1));
        }

        [Test]
        public void Save_TieneIlBackup() {
            Tree.AddArchivio();
            Archivio.Save(P("a.json"));
            Tree.AddArchivio("149", "Archivio di Stato di Napoli");
            Archivio.Save(P("a.json"));

            Assert.That(JArray.Parse(File.ReadAllText(P("a.json"))).Count, Is.EqualTo(2));
            Assert.That(JArray.Parse(File.ReadAllText(P("a.json.bak"))).Count, Is.EqualTo(1));
        }
    }
}
