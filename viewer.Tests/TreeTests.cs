using NUnit.Framework;
using System;
using System.Linq;
using System.Windows.Forms;

namespace viewer.Tests {

    /// <summary>Costruzione dell'albero di navigazione dalle cache (RegNode)</summary>
    [TestFixture, Apartment(System.Threading.ApartmentState.STA)]
    public class TreeTests {

        [SetUp]
        public void SetUp() { Tree.Reset(); }

        static string Path(TreeNode n) {
            string s = n.Text;
            while ((n = n.Parent) != null) s = n.Text + "/" + s;
            return s;
        }

        [Test]
        public void FillTree_GerarchiaCompleta() {
            Tree.Bari();
            Tree.AddAnno();
            Tree.AddKind();
            Tree.AddRegistro();

            using (var tv = new TreeView()) {
                RegNode.fillTree(tv.Nodes);

                Assert.That(tv.Nodes.Count, Is.EqualTo(1));
                var reg = tv.Nodes[0].Nodes[0].Nodes[0].Nodes[0].Nodes[0].Nodes[0];
                Assert.That(Path(reg), Is.EqualTo("Archivio di Stato di Bari/Stato civile napoleonico/Bari/1813/Nati/Parte 1"));

                var tag = (RegNode)reg.Tag;
                Assert.That(tag.tipo, Is.EqualTo(tipoNodo.Registro));
                Assert.That(tag.key, Is.EqualTo("282248"));
                Assert.That(reg.ToolTipText, Is.EqualTo("282248"));
            }
        }

        [Test]
        public void FillTree_NessunDuplicato_ESvuotaPrima() {
            Tree.Bari();
            Tree.AddAnno();
            Tree.AddKind();
            Tree.AddRegistro("282216", "1813", "Nati", "282248", "Parte 1");
            Tree.AddRegistro("282216", "1813", "Nati", "282249", "Parte 2");
            Tree.AddFondo("208", "3804025", "Stato civile italiano");

            using (var tv = new TreeView()) {
                tv.Nodes.Add("vecchio");
                RegNode.fillTree(tv.Nodes);

                Assert.That(tv.Nodes.Count, Is.EqualTo(1), "un solo archivio, il nodo vecchio e' sparito");
                Assert.That(tv.Nodes[0].Nodes.Count, Is.EqualTo(2), "due fondi");
                var kind = tv.Nodes[0].Nodes[0].Nodes[0].Nodes[0].Nodes[0];
                Assert.That(kind.Nodes.Cast<TreeNode>().Select(n => n.Text), Is.EqualTo(new[] { "Parte 1", "Parte 2" }));
            }
        }

        [Test]
        public void FillTree_AnniOrdinatiCronologicamente() {
            Tree.Bari();
            foreach (var a in new[] { "1820", "1809-1810", "1815", "01/03/1812" })
                Tree.AddAnno("282216", a);

            using (var tv = new TreeView()) {
                RegNode.fillTree(tv.Nodes);
                var serie = tv.Nodes[0].Nodes[0].Nodes[0];
                Assert.That(serie.Nodes.Cast<TreeNode>().Select(n => n.Text),
                    Is.EqualTo(new[] { "1809-1810", "01/03/1812", "1815", "1820" }));
            }
        }

        [TestCase("1813", 1813, 1, 1)]
        [TestCase("1809-1810", 1809, 1, 1)]
        [TestCase("05/04/1820", 1820, 4, 5)]
        [TestCase("05/04/1820-10/10/1821", 1820, 4, 5)]
        public void ParseFirstDate(string caption, int y, int m, int d) {
            Assert.That(Priv.Call<DateTime>(typeof(RegNode), "ParseFirstDate", caption), Is.EqualTo(new DateTime(y, m, d)));
        }

        [Test]
        public void RegNode_TitoloCollegatoAllaDescrizione() {
            Tree.Bari();
            var rn = new RegNode(Serie.serieById["282216"]);
            rn.title = "Bari (città)";
            Assert.That(Serie.serieById["282216"].description, Is.EqualTo("Bari (città)"), "rinominare il nodo rinomina la serie");
        }
    }
}
