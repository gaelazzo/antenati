using NUnit.Framework;
using System.IO;
using System.Linq;

namespace viewer.Tests {

    /// <summary>
    /// Ri-esplorare un nodo (doppio click: rilegge sempre dal sito) non deve duplicare i nodi
    /// ne' perdere i dati dell'utente attaccati a quelli gia' noti.
    /// </summary>
    [TestFixture]
    public class ReExploreTests {

        [SetUp]
        public void SetUp() { Tree.Reset(); }

        static Registro RegistroConDatiUtente() {
            Tree.Bari();
            Tree.AddAnno();
            Tree.AddKind();
            var r = Tree.AddRegistro("282216", "1813", "Nati", "282248", "Parte 1");
            r.manifestId = "wOgD6pM";
            r.idRegistroIndice = "999";
            r.nPaginaIndice = 12;
            r.lastPageViewed = "LqaVpAR";
            return r;
        }

        [Test]
        public void Registri_RiEsplorati_ConservanoIDatiUtente() {
            var old = RegistroConDatiUtente();
            Registro.parseRegistri("1813", "282216", "Nati", Sandbox.ReadFixture("serie_282216_1813_Nati.html"));

            var r = Registro.registroById["282248"];
            Assert.That(r, Is.SameAs(old), "registroById deve restare sull'istanza che sta nell'albero");
            Assert.That(r.idRegistroIndice, Is.EqualTo("999"));
            Assert.That(r.lastPageViewed, Is.EqualTo("LqaVpAR"));
            Assert.That(Registro.registri.Count(x => x.idRegistro == "282248"), Is.EqualTo(1));
        }

        [Test]
        public void Registri_RiEsplorati_DopoSalvataggioERicarica() {
            RegistroConDatiUtente();
            Registro.parseRegistri("1813", "282216", "Nati", Sandbox.ReadFixture("serie_282216_1813_Nati.html"));
            string f = Path.Combine(Sandbox.NewDir(), "r.json");
            Registro.Save(f);
            Registro.registri.Clear(); Registro.registroById.Clear();
            Registro.Load(f);

            Assert.That(Registro.registroById["282248"].idRegistroIndice, Is.EqualTo("999"));
            Assert.That(Registro.registroById["282248"].manifestId, Is.EqualTo("wOgD6pM"));
        }

        [Test]
        public void Registri_RisultatoUnico_RiEsplorato() {
            var old = RegistroConDatiUtente();
            Registro.deriveRegistroFromPage("1813", "282216", "Nati", "https://antenati.cultura.gov.it/ark:/12657/an_ua282248/");
            Assert.That(Registro.registroById["282248"], Is.SameAs(old));
            Assert.That(Registro.registri.Count, Is.EqualTo(1));
        }

        // ---------- registri sotto piu' anni (indici decennali: 1876 e 1885) ----------

        static void DueAnniDecennali() {
            Tree.Bari();
            Tree.AddAnno("282216", "1876");
            Tree.AddAnno("282216", "1885");
            Tree.AddKind("282216", "1876", "Nati, indici decennali");
            Tree.AddKind("282216", "1885", "Nati, indici decennali");
        }

        static string Decennale(string id, string titolo) =>
            $@"<li class=""search-item"" data-id=""{id}""><div><h3><a href=""/ark:/12657/an_ua{id}"">Registro: {titolo}</a></h3>
               <p><strong>Segnatura attuale:</strong> (A-K)</p></div></li>";

        [Test]
        public void Decennale_SottoDueAnni_UnaPosizionePerAnno() {
            DueAnniDecennali();
            Registro.parseRegistri("1876", "282216", "Nati, indici decennali", Decennale("877232", "1876-1885"));
            Registro.parseRegistri("1885", "282216", "Nati, indici decennali", Decennale("877232", "1876-1885"));

            Assert.That(Registro.registri.Select(r => r.parentKey),
                Is.EquivalentTo(new[] { "282216/1876/Nati, indici decennali", "282216/1885/Nati, indici decennali" }));
            using (var tv = new System.Windows.Forms.TreeView()) {
                RegNode.fillTree(tv.Nodes);
                var serie = tv.Nodes[0].Nodes[0].Nodes[0];
                Assert.That(serie.Nodes.Cast<System.Windows.Forms.TreeNode>().Select(n => n.Nodes[0].Nodes.Count),
                    Is.EqualTo(new[] { 1, 1 }), "il registro compare sotto entrambi gli anni");
            }
        }

        [Test]
        public void Decennale_NuovaPosizione_EreditaIDatiUtente() {
            DueAnniDecennali();
            Registro.parseRegistri("1876", "282216", "Nati, indici decennali", Decennale("877232", "1876-1885"));
            Registro.registroById["877232"].lastPageViewed = "40";

            var r = (Registro)Registro.parseRegistri("1885", "282216", "Nati, indici decennali", Decennale("877232", "1876-1885")).Single();
            Assert.That(r.lastPageViewed, Is.EqualTo("40"));
        }

        [Test]
        public void Decennale_SalvataggioAllineaLePosizioni() {
            DueAnniDecennali();
            Registro.parseRegistri("1876", "282216", "Nati, indici decennali", Decennale("877232", "1876-1885"));
            Registro.parseRegistri("1885", "282216", "Nati, indici decennali", Decennale("877232", "1876-1885"));
            // il codice scrive i dati dell'utente sull'istanza di registroById
            Registro.registroById["877232"].idRegistroIndice = "877232";
            Registro.registroById["877232"].nPaginaIndice = 5;

            string f = Path.Combine(Sandbox.NewDir(), "r.json");
            Registro.Save(f);
            Registro.registri.Clear(); Registro.registroById.Clear();
            Registro.Load(f);

            Assert.That(Registro.registri.Count, Is.EqualTo(2));
            Assert.That(Registro.registri.All(r => r.nPaginaIndice == 5), Is.True);
            Assert.That(Registro.registroById["877232"].nPaginaIndice, Is.EqualTo(5));
        }

        // ---------- file salvati prima della correzione ----------

        [Test]
        public void Load_CopiaVuotaDopoQuellaConLIndice_IndiceConservato() {
            // Caso reale dei file vecchi: l'utente collega l'indice, poi una ri-esplorazione aggiunge
            // in coda una copia vuota. "Vince l'ultima copia" perdeva l'indice (e il Save lo cancellava
            // per sempre): per ogni dato vale l'ultimo valore non vuoto.
            Tree.Bari();
            Tree.AddAnno();
            Tree.AddKind();
            string f = Path.Combine(Sandbox.NewDir(), "r.json");
            File.WriteAllText(f, @"[
              {""description"":""unico"",""idSerie"":""282216"",""kind"":""Nati"",""anno"":""1813"",""idRegistro"":""282248"",""manifestId"":""wOgD6pM"",""idRegistroIndice"":""999"",""nPaginaIndice"":12,""lastPageViewed"":""50""},
              {""description"":""unico"",""idSerie"":""282216"",""kind"":""Nati"",""anno"":""1813"",""idRegistro"":""282248"",""manifestId"":null,""idRegistroIndice"":null,""nPaginaIndice"":0,""lastPageViewed"":""70""}
            ]");
            Registro.Load(f);

            var r = Registro.registroById["282248"];
            Assert.That(r.idRegistroIndice, Is.EqualTo("999"));
            Assert.That(r.nPaginaIndice, Is.EqualTo(12));
            Assert.That(r.manifestId, Is.EqualTo("wOgD6pM"));
            Assert.That(r.lastPageViewed, Is.EqualTo("70"), "qui la copia piu' recente ha un valore: vince lei");

            // e il salvataggio non lo perde
            Registro.Save(f);
            Registro.registri.Clear(); Registro.registroById.Clear();
            Registro.Load(f);
            Assert.That(Registro.registroById["282248"].idRegistroIndice, Is.EqualTo("999"));
        }

        [Test]
        public void Load_RegistroInDuePosizioni_RegistroByIdSullUltima() {
            // come prima di bf1a42a: gotoRegister di un decennale seleziona l'ultima posizione del file
            DueAnniDecennali();
            string f = Path.Combine(Sandbox.NewDir(), "r.json");
            File.WriteAllText(f, @"[
              {""description"":""1876-1885"",""idSerie"":""282216"",""kind"":""Nati, indici decennali"",""anno"":""1876"",""idRegistro"":""877232""},
              {""description"":""1876-1885"",""idSerie"":""282216"",""kind"":""Nati, indici decennali"",""anno"":""1885"",""idRegistro"":""877232""}
            ]");
            Registro.Load(f);
            Assert.That(Registro.registroById["877232"].anno, Is.EqualTo("1885"));
        }

        [Test]
        public void Load_FileConCopie_DatiDallUltimaDescrizioneDallaPrima() {
            Tree.Bari();
            Tree.AddAnno();
            Tree.AddKind();
            string f = Path.Combine(Sandbox.NewDir(), "r.json");
            File.WriteAllText(f, @"[
              {""description"":""morti nati"",""idSerie"":""282216"",""kind"":""Nati"",""anno"":""1813"",""idRegistro"":""282248"",""manifestId"":null,""lastPageViewed"":null},
              {""description"":""unico"",""idSerie"":""282216"",""kind"":""Nati"",""anno"":""1813"",""idRegistro"":""282248"",""manifestId"":""wOgD6pM"",""idRegistroIndice"":""999"",""nPaginaIndice"":12,""lastPageViewed"":""138""}
            ]");
            Registro.Load(f);

            var r = Registro.registri.Single();
            Assert.That(Registro.registroById["282248"], Is.SameAs(r));
            Assert.That(r.description, Is.EqualTo("morti nati"), "la descrizione che l'albero mostrava gia'");
            Assert.That(r.manifestId, Is.EqualTo("wOgD6pM"));
            Assert.That(r.idRegistroIndice, Is.EqualTo("999"));
            Assert.That(r.nPaginaIndice, Is.EqualTo(12));
            Assert.That(r.lastPageViewed, Is.EqualTo("138"));
        }

        [Test]
        public void Load_TipologieConCopie_TieneLaPrima() {
            Tree.Bari();
            Tree.AddAnno();
            string f = Path.Combine(Sandbox.NewDir(), "k.json");
            File.WriteAllText(f, @"[{""description"":""Nati"",""idSerie"":""282216"",""kind"":""Nati"",""anno"":""1813""},
                                    {""description"":""Nati2"",""idSerie"":""282216"",""kind"":""Nati"",""anno"":""1813""}]");
            AnnoSerieKind.Load(f);
            Assert.That(AnnoSerieKind.annoSeriesKind.Count, Is.EqualTo(1));
            Assert.That(AnnoSerieKind.annoSerieKindById["282216/1813/Nati"].description, Is.EqualTo("Nati"));
        }

        [Test]
        public void Load_AnniConCopie_SerieSenzaCopie() {
            var serie = Tree.Bari();
            string f = Path.Combine(Sandbox.NewDir(), "a.json");
            File.WriteAllText(f, @"[{""idSerie"":""282216"",""anno"":""1813""},{""idSerie"":""282216"",""anno"":""1813""}]");
            AnnoSerie.Load(f);
            Assert.That(serie.myAnnoSeries.Count, Is.EqualTo(1));
            Assert.That(serie.myAnnoSeries[0], Is.SameAs(AnnoSerie.annoSerieById["282216/1813"]));
        }

        [Test]
        public void Fondi_RiEsplorati_NonDuplicano() {
            Tree.AddArchivio();
            string html = Sandbox.ReadFixture("archivio_208.html");
            Fondo.parseFondi("208", html);
            var f = Fondo.fondoById["281500"];
            Fondo.parseFondi("208", html);

            Assert.That(Fondo.fondi.Count, Is.EqualTo(4));
            Assert.That(Fondo.fondoById["281500"], Is.SameAs(f));
        }

        [Test]
        public void Anni_RiEsplorati_NonDuplicano() {
            var serie = Tree.Bari();
            string html = Sandbox.ReadFixture("serie_282216.html");
            AnnoSerie.parseAnni("282216", html);
            var a = AnnoSerie.annoSerieById["282216/1813"];
            AnnoSerie.parseAnni("282216", html);

            Assert.That(AnnoSerie.annoSeries.Count, Is.EqualTo(7));
            Assert.That(serie.myAnnoSeries.Count, Is.EqualTo(7));
            Assert.That(AnnoSerie.annoSerieById["282216/1813"], Is.SameAs(a));
        }

        [Test]
        public void Tipologie_RisultatoUnico_RiEsplorate_NonDuplicano() {
            Tree.Bari();
            string html = Sandbox.ReadFixture("registro_282248.html");
            string url = "https://antenati.cultura.gov.it/ark:/12657/an_ua282248/";
            AnnoSerie.deriveAnnoSerieFromRegistroPage("282216", html, url);
            var k = AnnoSerieKind.annoSerieKindById["282216/1813/Nati"];
            AnnoSerie.deriveAnnoSerieFromRegistroPage("282216", html, url);

            Assert.That(AnnoSerieKind.annoSeriesKind.Count, Is.EqualTo(1));
            Assert.That(AnnoSerieKind.annoSerieKindById["282216/1813/Nati"], Is.SameAs(k));
            Assert.That(Registro.registri.Count, Is.EqualTo(1));
        }
    }
}
