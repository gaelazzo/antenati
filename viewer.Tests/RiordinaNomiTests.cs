using NUnit.Framework;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace viewer.Tests {

    /// <summary>
    /// "Riordina nomi": ordina i figli di un nodo per data quando i nomi sono date o intervalli
    /// (es. registri "07/03/1840-30/04/1840"). Prima un solo nome non-data ("(A-I)", "10118, Parte 1",
    /// un nodo rinominato) mandava tutto in FormatException.
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA)]
    public class RiordinaNomiTests {

        static string[] Riordina(params string[] nomi) {
            var parent = new TreeNode("tipologia");
            foreach (var n in nomi) parent.Nodes.Add(n);
            RegNode.rielaboraNomi(parent);
            return parent.Nodes.Cast<TreeNode>().Select(n => n.Text).ToArray();
        }

        [Test]
        public void IntervalliDiDate_InOrdineCronologico() {
            // i registri reali di Montecalvario 1840, Matrimoni, processetti, nell'ordine del sito
            var r = Riordina("02/01/1840-28/01/1840", "03/07/1840-31/08/1840", "04/11/1840-31/12/1840",
                             "05/09/1840-30/10/1840", "07/03/1840-30/04/1840", "07/05/1840-22/06/1840",
                             "22/06/1840-27/06/1840", "28/01/1840-29/02/1840");
            Assert.That(r, Is.EqualTo(new[] {
                "02/01/1840-28/01/1840", "28/01/1840-29/02/1840", "07/03/1840-30/04/1840", "07/05/1840-22/06/1840",
                "22/06/1840-27/06/1840", "03/07/1840-31/08/1840", "05/09/1840-30/10/1840", "04/11/1840-31/12/1840" }));
        }

        [Test]
        public void AnniEIntervalliDiAnni() {
            Assert.That(Riordina("1885", "1876-1885", "1813"), Is.EqualTo(new[] { "1813", "1876-1885", "1885" }));
        }

        [Test]
        public void NomiNonData_InFondoInOrdineAlfabetico_SenzaEccezione() {
            string[] r = null;
            Assert.That(() => r = Riordina("(L-Z)", "05/09/1840-30/10/1840", "10118, Parte 1", "(A-I)", "02/01/1840-28/01/1840"), Throws.Nothing);
            Assert.That(r, Is.EqualTo(new[] { "02/01/1840-28/01/1840", "05/09/1840-30/10/1840", "(A-I)", "(L-Z)", "10118, Parte 1" }));
        }

        [TestCase(" 07/03/1840 - 30/04/1840 ", "02/01/1840-28/01/1840", ExpectedResult = new[] { "02/01/1840-28/01/1840", " 07/03/1840 - 30/04/1840 " })]
        [TestCase("7/3/1840", "28/1/1840", ExpectedResult = new[] { "28/1/1840", "7/3/1840" })]
        public string[] SpaziEGiorniAUnaCifra_LettiComeDate(string a, string b) => Riordina(a, b);

        [Test]
        public void StessaData_OrdineOriginaleConservato() {
            Assert.That(Riordina("1840 bis", "1840", "1813"), Is.EqualTo(new[] { "1813", "1840 bis", "1840" }));
        }

        [Test]
        public void ViewerUsaLaStessaRegola() {
            // il menu "Riordina nomi" del Viewer chiamava una sua copia di ParseFirstDate
            var parent = new TreeNode("p");
            foreach (var n in new[] { "(A-I)", "1885", "1813" }) parent.Nodes.Add(n);
            Assert.That(() => Priv.Call(Priv.NewViewer(), "rielaboraNomi", parent), Throws.Nothing);
            Assert.That(parent.Nodes.Cast<TreeNode>().Select(n => n.Text), Is.EqualTo(new[] { "1813", "1885", "(A-I)" }));
        }
    }
}
