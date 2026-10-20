using NUnit.Framework;

namespace viewer.Tests {

    /// <summary>
    /// Menu dell'albero "imposta n. pagina indice": cambia la pagina dell'indice gia' collegato al
    /// registro (quella a cui porta "vai all'indice"). Prima la condizione era invertita: un numero
    /// cancellava il collegamento e 0 lo creava con pagina 0 (che per il resto del programma vuol dire
    /// "nessuna pagina"); senza indice collegato andava in NullReferenceException.
    /// </summary>
    [TestFixture]
    public class IndicePaginaTests {
        Registro reg, indice;

        [SetUp]
        public void SetUp() {
            Tree.Reset();
            Tree.Bari();
            Tree.AddAnno();
            Tree.AddKind();
            reg = Tree.AddRegistro(idRegistro: "282248", segnatura: "Parte 1");
            indice = Tree.AddRegistro(idRegistro: "282300", segnatura: "indice");
            reg.idRegistroIndice = indice.idRegistro;
            reg.nPaginaIndice = 7;
        }

        string Set(Registro r, Registro idx, int page) => Priv.Call<string>(typeof(Viewer), "setIndexPage", r, idx, page);

        [Test]
        public void Numero_DiventaLaPaginaIndice_IlCollegamentoResta() {
            Assert.That(Set(reg, indice, 12), Is.Null);
            Assert.That(reg.nPaginaIndice, Is.EqualTo(12));
            Assert.That(reg.idRegistroIndice, Is.EqualTo("282300"));
        }

        [Test]
        public void Zero_TogliIlCollegamento() {
            Assert.That(Set(reg, indice, 0), Is.Null);
            Assert.That(reg.nPaginaIndice, Is.EqualTo(0));
            Assert.That(reg.idRegistroIndice, Is.Null);
        }

        [Test]
        public void IndiceInterno_StessoRegistro() {
            // collegamento fatto con "imposta indice interno": l'indice e' il registro stesso
            reg.idRegistroIndice = reg.idRegistro;
            Assert.That(Set(reg, reg, 3), Is.Null);
            Assert.That(reg.nPaginaIndice, Is.EqualTo(3));
            Assert.That(reg.idRegistroIndice, Is.EqualTo("282248"));
        }

        [Test]
        public void SenzaIndiceCollegato_Messaggio_NienteEccezione() {
            reg.idRegistroIndice = null;
            reg.nPaginaIndice = 0;
            string msg = null;
            Assert.That(() => msg = Set(reg, null, 12), Throws.Nothing);
            Assert.That(msg, Does.Contain("indice"));
            Assert.That(reg.nPaginaIndice, Is.EqualTo(0), "niente pagina senza un indice a cui riferirla");
        }

        [Test]
        public void SenzaRegistro_Messaggio() {
            Assert.That(Set(null, indice, 12), Is.Not.Null);
        }

        // ---------- menu dell'albero "imposta pagina": indice interno, come "imposta indice interno" ----------
        // Prima scriveva il numero nel Tag del nodo (al posto del RegNode): nessuno lo rileggeva e il nodo
        // perdeva l'identita' di registro fino al riavvio (eccezione riselezionandolo).

        string SetInternal(System.Windows.Forms.TreeNode n, int page) =>
            Priv.Call<string>(Priv.NewViewer(), "setInternalIndexPage", n, page);

        [Test]
        public void ImpostaPagina_IndiceNelloStessoRegistro() {
            var node = new System.Windows.Forms.TreeNode("Parte 1") { Tag = new RegNode(reg) };
            var tag = node.Tag;
            Assert.That(SetInternal(node, 15), Is.Null);
            Assert.That(reg.idRegistroIndice, Is.EqualTo("282248"), "l'indice e' il registro stesso");
            Assert.That(reg.nPaginaIndice, Is.EqualTo(15));
            Assert.That(node.Tag, Is.SameAs(tag), "il Tag del nodo non va toccato");
        }

        [Test]
        public void ImpostaPagina_Zero_TogliIlCollegamento() {
            var node = new System.Windows.Forms.TreeNode("Parte 1") { Tag = new RegNode(reg) };
            Assert.That(SetInternal(node, 0), Is.Null);
            Assert.That(reg.idRegistroIndice, Is.Null);
            Assert.That(reg.nPaginaIndice, Is.EqualTo(0));
        }

        [Test]
        public void ImpostaPagina_NodoCheNonERegistro_Messaggio() {
            var node = new System.Windows.Forms.TreeNode("Bari") { Tag = new RegNode(Serie.serieById["282216"]) };
            string msg = null;
            Assert.That(() => msg = SetInternal(node, 15), Throws.Nothing);
            Assert.That(msg, Does.Contain("registro"));
            Assert.That(reg.nPaginaIndice, Is.EqualTo(7), "nessun registro modificato");
        }
    }
}
