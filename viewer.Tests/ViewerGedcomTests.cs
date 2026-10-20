using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace viewer.Tests {

    /// <summary>
    /// Collegamento tra la scheda di ricerca del Viewer e GedcomSearch: ogni casella deve accendere
    /// l'opzione giusta, e i campi devono mostrare quello che il modulo restituisce.
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA)]
    public class ViewerGedcomTests {
        Viewer v;
        GedcomSearch g;
        string file;

        T Box<T>(string name) where T : Control, new() {
            var c = new T();
            Priv.Set(v, name, c);
            return c;
        }

        [SetUp]
        public void SetUp() {
            v = Priv.NewViewer();
            foreach (var chk in new[] { "chkFullNames", "chkFullDate", "chkFullKey", "chkKeys", "chkShowSpouse" })
                Box<CheckBox>(chk);
            foreach (var txt in new[] { "txtSearchName", "txtSearchResult", "txtID", "txtLongFam" })
                Box<TextBox>(txt);
            file = Path.Combine(Sandbox.FixturesDir, "ricerca.ged");
            Assert.That(Priv.Call<bool>(v, "readGedFile", file), Is.True);
            g = GedcomSearch.Load(file);
        }

        CheckBox Chk(string n) => (CheckBox)Priv.Get(v, n);
        TextBox Txt(string n) => (TextBox)Priv.Get(v, n);

        [TestCase("chkFullNames", nameof(DescriptionOptions.FullNames))]
        [TestCase("chkFullDate", nameof(DescriptionOptions.FullDates))]
        [TestCase("chkShowSpouse", nameof(DescriptionOptions.ShowSpouse))]
        [TestCase("chkKeys", nameof(DescriptionOptions.ShowKey))]
        [TestCase("chkFullKey", nameof(DescriptionOptions.ShowAllKeys))]
        public void OgniCasella_LaSuaOpzione(string casella, string opzione) {
            Chk(casella).Checked = true;
            var o = Priv.Call<DescriptionOptions>(v, "descriptionOptions");
            foreach (var f in typeof(DescriptionOptions).GetFields())
                Assert.That((bool)f.GetValue(o), Is.EqualTo(f.Name == opzione), f.Name);
        }

        [Test]
        public void Ricerca_RisultatoDelModuloNelCampo() {
            Chk("chkShowSpouse").Checked = true;
            Chk("chkFullDate").Checked = true;
            Txt("txtSearchName").Text = "Giuseppe Rossi";
            Priv.Call(v, "searchNames");
            var expected = g.Search("Giuseppe Rossi", new DescriptionOptions { ShowSpouse = true, FullDates = true });
            Assert.That(Txt("txtSearchResult").Text, Is.EqualTo(string.Concat(expected.Select(l => l + Environment.NewLine))));
        }

        [Test]
        public void Ricerca_CampoVuoto_RisultatoInvariato() {
            Txt("txtSearchResult").Text = "precedente";
            Txt("txtSearchName").Text = "";
            Priv.Call(v, "searchNames");
            Assert.That(Txt("txtSearchResult").Text, Is.EqualTo("precedente"));
        }

        [Test]
        public void Scheda_IdSenzaPrefisso() {
            string id = g.Search("Michele Rossi", new DescriptionOptions { ShowKey = true }).Single().Split(' ')[0];
            Txt("txtID").Text = id.Substring(4);
            Priv.Call(v, "longSearch");
            Assert.That(Txt("txtLongFam").Text, Is.EqualTo(g.Describe(id, new DescriptionOptions())));
        }

        [Test]
        public void Scheda_IdInesistente_CampoInvariato() {
            Txt("txtLongFam").Text = "precedente";
            Txt("txtID").Text = "99999";
            Priv.Call(v, "longSearch");
            Assert.That(Txt("txtLongFam").Text, Is.EqualTo("precedente"));
        }

        [Test]
        public void NomeDelFile_PerStartTxt() {
            Assert.That(((GedcomSearch)Priv.Get(v, "genealogy")).FileName, Is.EqualTo(file));
        }
    }
}
