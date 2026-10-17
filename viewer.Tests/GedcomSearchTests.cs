using Gedcom;
using NUnit.Framework;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace viewer.Tests {

    /// <summary>
    /// Ricerca nel GEDCOM con la sintassi della scheda di ricerca:
    /// "Nome Cognome DI padre E DI madre marito di/moglie di coniuge".
    /// </summary>
    [TestFixture, Apartment(System.Threading.ApartmentState.STA)]
    public class GedcomSearchTests {
        // Michele Rossi x Maria Concetta Bianchi (1815) -> Giuseppe Rossi x Anna Verdi (1845)
        // Antonio Rossi padre di Michele; Giuseppe Esposito senza famiglia; un secondo Giuseppe Rossi (I6, 1851) omonimo
        // RIN = XRef originale: Gedcom.NET rinumera gli XRef in lettura e i nomi non bastano a distinguere le persone
        const string Ged = @"0 HEAD
1 SOUR test
1 GEDC
2 VERS 5.5
2 FORM LINEAGE-LINKED
1 CHAR UTF-8
0 @I0@ INDI
1 RIN I0
1 NAME Antonio /Rossi/
1 SEX M
1 FAMS @F0@
0 @I1@ INDI
1 RIN I1
1 NAME Michele /Rossi/
1 SEX M
1 BIRT
2 DATE 1790
1 FAMC @F0@
1 FAMS @F1@
0 @I2@ INDI
1 RIN I2
1 NAME Maria Concetta /Bianchi/
1 SEX F
1 FAMS @F1@
0 @I3@ INDI
1 RIN I3
1 NAME Giuseppe /Rossi/
1 SEX M
1 BIRT
2 DATE 12 MAR 1820
1 DEAT
2 DATE 1880
1 FAMC @F1@
1 FAMS @F2@
0 @I4@ INDI
1 RIN I4
1 NAME Anna /Verdi/
1 SEX F
1 FAMS @F2@
0 @I5@ INDI
1 RIN I5
1 NAME Giuseppe /Esposito/
1 SEX M
0 @I6@ INDI
1 RIN I6
1 NAME Giuseppe /Rossi/
1 SEX M
1 BIRT
2 DATE 1851
0 @F0@ FAM
1 HUSB @I0@
1 CHIL @I1@
0 @F1@ FAM
1 HUSB @I1@
1 WIFE @I2@
1 CHIL @I3@
1 MARR
2 DATE 1815
0 @F2@ FAM
1 HUSB @I3@
1 WIFE @I4@
1 MARR
2 DATE 1845
0 TRLR
";
        Viewer v;

        [SetUp]
        public void SetUp() {
            v = Priv.NewViewer();
            foreach (var chk in new[] { "chkFullNames", "chkFullDate", "chkFullKey", "chkKeys", "chkShowSpouse" })
                Priv.Set(v, chk, new CheckBox());
            ((CheckBox)Priv.Get(v, "chkShowSpouse")).Checked = true;

            string file = Path.Combine(Sandbox.NewDir(), "test.ged");
            File.WriteAllText(file, Ged.Replace("\r\n", "\n").Replace("\n", "\r\n"), new UTF8Encoding(false));
            Assert.That(Priv.Call<bool>(v, "readGedFile", file), Is.True);
        }

        // Gedcom.NET rinumera gli XRef in lettura (@I3@ -> XREFn): l'identita' stabile e' il RIN
        static string Id(GedcomIndividualRecord i) => i.AutomatedRecordID;

        string[] Find(string mask) {
            return Priv.Call<System.Collections.Generic.IEnumerable<GedcomIndividualRecord>>(v, "findIndividual", mask)
                       .Select(Id).OrderBy(x => x).ToArray();
        }

        GedcomIndividualRecord Person(string rin) {
            return ((GedcomDatabase)Priv.Get(v, "database")).Individuals.Single(i => Id(i) == rin);
        }

        [Test]
        public void NomeECognome_TrovaGliOmonimi() {
            Assert.That(Find("Giuseppe Rossi"), Is.EqualTo(new[] { "I3", "I6" }));
        }

        [Test]
        public void Omonimi_DistintiDalPadre() {
            Assert.That(Find("Giuseppe Rossi DI Michele"), Is.EqualTo(new[] { "I3" }));
        }

        [Test]
        public void CognomeENomeInvertiti() {
            Assert.That(Find("Rossi Giuseppe"), Is.EqualTo(new[] { "I3", "I6" }));
        }

        [Test]
        public void ConPadre() {
            Assert.That(Find("Giuseppe Rossi DI Michele"), Is.EqualTo(new[] { "I3" }));
            Assert.That(Find("Giuseppe Rossi DI Antonio"), Is.Empty);
        }

        [Test]
        public void ConNonnoPaterno() {
            Assert.That(Find("Giuseppe Rossi DI Michele DI Antonio"), Is.EqualTo(new[] { "I3" }));
            Assert.That(Find("Giuseppe Rossi DI Michele DI Pasquale"), Is.Empty);
        }

        [Test]
        public void ConMadre() {
            Assert.That(Find("Giuseppe Rossi DI Michele E DI Maria Concetta Bianchi"), Is.EqualTo(new[] { "I3" }));
            Assert.That(Find("Giuseppe Rossi DI Michele E DI Anna Verdi"), Is.Empty);
        }

        [Test]
        public void ConConiuge() {
            Assert.That(Find("Giuseppe Rossi marito di Anna Verdi"), Is.EqualTo(new[] { "I3" }));
            Assert.That(Find("Anna Verdi moglie di Giuseppe Rossi"), Is.EqualTo(new[] { "I4" }));
            Assert.That(Find("Giuseppe Esposito marito di Anna Verdi"), Is.Empty);
        }

        [Test]
        public void CognomeSconosciuto_ConPadre() {
            Assert.That(Find("Giuseppe ? DI Michele"), Is.EqualTo(new[] { "I3" }));
        }

        [Test]
        public void DescrizioneBreve() {
            var indi = Person("I3");
            Assert.That(Priv.Call<string>(v, "getSimpleDescr", indi),
                Is.EqualTo("Giuseppe Rossi(1820-1880) DI Michele(1790) E DI Maria Bianchi[1815], marito di Anna Verdi[1845]"));
        }

        [Test]
        public void DescrizioneBreve_DateENomiCompleti() {
            ((CheckBox)Priv.Get(v, "chkFullDate")).Checked = true;
            ((CheckBox)Priv.Get(v, "chkFullNames")).Checked = true;
            ((CheckBox)Priv.Get(v, "chkKeys")).Checked = true;
            var indi = Person("I3");
            string s = Priv.Call<string>(v, "getSimpleDescr", indi);

            Assert.That(s, Does.StartWith(indi.XRefID + " Giuseppe Rossi(12 MAR 1820-1880)"));
            Assert.That(s, Does.Contain("E DI Maria Concetta Bianchi"));
        }

        [Test]
        public void Figli() {
            var indi = Person("I1");
            string s = Priv.Call<string>(v, "children", indi);
            Assert.That(s, Does.Contain("with Maria Bianchi:"));
            Assert.That(s, Does.Contain("Giuseppe Rossi(1820-1880), marito di Anna Verdi married in 1845"));
        }
    }
}
