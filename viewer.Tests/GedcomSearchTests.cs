using Newtonsoft.Json;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace viewer.Tests {

    /// <summary>
    /// Ricerca nel GEDCOM (GedcomSearch) con la sintassi della scheda di ricerca:
    /// "Nome Cognome DI padre DI nonno E DI madre DI suo padre marito di/moglie di coniuge".
    /// File di prova Fixtures\ricerca.ged:
    ///   Antonio Rossi -> Michele Rossi (1790) x Maria Concetta Bianchi [1815] -> Giuseppe Rossi (1820-1880) x Anna Verdi (ABT 1825) [1845]
    ///   figli di Giuseppe e Anna: Giovanni Del Vecchio (1846), Carmela Rossi (1848)
    ///   Rosa Maria / Rosina Neri (1830, due nomi, famiglia senza padre) -> Luigi Neri x Carmela Rossi [20 OTT 1870]
    ///   Giuseppe Esposito (senza famiglia), Giuseppe Rossi (1851, omonimo)
    /// Gli identificativi XREF vengono rinumerati in lettura: le persone si riconoscono dagli anni.
    /// </summary>
    [TestFixture]
    public class GedcomSearchTests {
        GedcomSearch g;
        static readonly DescriptionOptions conConiugi = new DescriptionOptions { ShowSpouse = true };

        [SetUp]
        public void SetUp() {
            g = GedcomSearch.Load(Path.Combine(Sandbox.FixturesDir, "ricerca.ged"));
            Assert.That(g, Is.Not.Null);
        }

        IReadOnlyList<string> Find(string q) => g.Search(q, conConiugi);

        [Test]
        public void FileNonValido_Null() {
            string f = Path.Combine(Sandbox.NewDir(), "rotto.ged");
            File.WriteAllText(f, "non e' un gedcom");
            Assert.That(GedcomSearch.Load(f), Is.Null);
        }

        [Test]
        public void NomeDelFile() {
            Assert.That(g.FileName, Does.EndWith("ricerca.ged"));
        }

        [Test]
        public void NomeECognome_TrovaGliOmonimi() {
            var r = Find("Giuseppe Rossi");
            Assert.That(r.Count, Is.EqualTo(2));
            Assert.That(r, Has.One.StartsWith("Giuseppe Rossi(1820-1880)"));
            Assert.That(r, Has.One.EqualTo("Giuseppe Rossi(1851)"));
        }

        [Test]
        public void CognomeENomeInvertiti() {
            Assert.That(Find("Rossi Giuseppe"), Is.EquivalentTo(Find("Giuseppe Rossi")));
        }

        [Test]
        public void ConPadre_DistingueGliOmonimi() {
            Assert.That(Find("Giuseppe Rossi DI Michele").Single(), Does.StartWith("Giuseppe Rossi(1820-1880)"));
            Assert.That(Find("Giuseppe Rossi DI Antonio"), Is.Empty);
        }

        [Test]
        public void ConNonnoPaterno() {
            Assert.That(Find("Giuseppe Rossi DI Michele DI Antonio").Count, Is.EqualTo(1));
            Assert.That(Find("Giuseppe Rossi DI Michele DI Pasquale"), Is.Empty);
        }

        [Test]
        public void ConMadre() {
            Assert.That(Find("Giuseppe Rossi DI Michele E DI Maria Concetta Bianchi").Count, Is.EqualTo(1));
            Assert.That(Find("Giuseppe Rossi DI Michele E DI Anna Verdi"), Is.Empty);
        }

        [Test]
        public void ConConiuge() {
            Assert.That(Find("Giuseppe Rossi marito di Anna Verdi").Single(), Does.StartWith("Giuseppe Rossi(1820-1880)"));
            Assert.That(Find("Anna Verdi moglie di Giuseppe Rossi").Single(), Does.StartWith("Anna Verdi(1825)"));
            Assert.That(Find("Giuseppe Esposito marito di Anna Verdi"), Is.Empty);
        }

        [Test]
        public void CognomeSconosciuto_ConPadre() {
            Assert.That(Find("Giuseppe ? DI Michele").Single(), Does.StartWith("Giuseppe Rossi(1820-1880)"));
        }

        [Test]
        public void CognomeConPrefisso() {
            Assert.That(Find("Giovanni Del Vecchio").Single(), Does.StartWith("Giovanni Del Vecchio(1846)"));
        }

        [Test]
        public void MadreSenzaPadreNellaFamiglia() {
            Assert.That(Find("Luigi Neri").Single(), Does.StartWith("Luigi Neri DI Rosa Neri(1830)"));
        }

        [Test]
        public void DescrizioneBreve() {
            Assert.That(Find("Giuseppe Rossi DI Michele").Single(),
                Is.EqualTo("Giuseppe Rossi(1820-1880) DI Michele(1790) E DI Maria Bianchi[1815], marito di Anna Verdi(1825)[1845]"));
        }

        [Test]
        public void DescrizioneBreve_DateNomiEIdentificativo() {
            var o = new DescriptionOptions { ShowSpouse = true, FullDates = true, FullNames = true, ShowKey = true };
            string s = g.Search("Giuseppe Rossi DI Michele", o).Single();
            Assert.That(s, Does.Match(@"^XREF\d+ Giuseppe Rossi\(12 MAR 1820-1880\)"));
            Assert.That(s, Does.Contain("E DI Maria Concetta Bianchi"));
        }

        [Test]
        public void SenzaConiugi_NonLiMostra() {
            Assert.That(g.Search("Giuseppe Rossi DI Michele", new DescriptionOptions()).Single(), Does.Not.Contain("marito di"));
        }

        [Test]
        public void Scheda_FigliEConiugi() {
            string id = IdOf("Michele Rossi");
            string s = g.Describe(id, conConiugi);
            Assert.That(s, Does.Contain("with Maria Bianchi:"));
            Assert.That(s, Does.Contain("Giuseppe Rossi(1820-1880), marito di Anna Verdi(1825) married in 1845"));
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase(null)]
        [TestCase("XREF99999")]
        public void Scheda_IdVuotoOInesistente_Null(string id) {
            Assert.That(g.Describe(id, conConiugi), Is.Null);
        }

        [Test]
        public void Scheda_IdConOSenzaPrefisso() {
            // la scheda accetta l'identificativo con o senza "XREF" (prima lo aggiungeva il Viewer)
            string id = IdOf("Michele Rossi");
            string num = id.Substring(4);
            Assert.That(g.Describe(num, conConiugi), Is.EqualTo(g.Describe(id, conConiugi)));
            Assert.That(g.Describe(" " + num + " ", conConiugi), Is.EqualTo(g.Describe(id, conConiugi)));
        }

        string IdOf(string query) =>
            g.Search(query, new DescriptionOptions { ShowKey = true }).Single().Split(' ')[0];

        // ---------- regole sui nomi ----------

        [TestCase("12 MAR 1820", "1820")]
        [TestCase("ABT 1790", "1790")]
        [TestCase("1813", "1813")]
        [TestCase("", "")]
        [TestCase(null, "")]
        public void Anno(string date, string expected) {
            Assert.That(Priv.Call<string>(typeof(GedcomSearch), "getYear", date), Is.EqualTo(expected));
        }

        [TestCase("De", true)]
        [TestCase("di", true)]
        [TestCase("Del", true)]
        [TestCase("Rossi", false)]
        [TestCase("Ann", false)]
        [TestCase("", false)]
        public void Prefisso(string s, bool expected) {
            Assert.That(Priv.Call<bool>(typeof(GedcomSearch), "isPrefix", s), Is.EqualTo(expected));
        }

        [TestCase("Giuseppe Rossi", "Giuseppe", "Rossi")]
        [TestCase("Maria Concetta Bianchi", "Maria Concetta", "Bianchi")]
        [TestCase("Maria De Rosa", "Maria", "De Rosa")]
        [TestCase("Giovanni Del Vecchio", "Giovanni", "Del Vecchio")]
        [TestCase("? Rossi", "?", "Rossi")]
        [TestCase("Giuseppe ?", "Giuseppe", "?")]
        [TestCase("Rossi", "?", "Rossi")]
        [TestCase("  Giuseppe Rossi  ", "Giuseppe", "Rossi")]
        public void NomiECognome(string s, string names, string surname) {
            Assert.That(Priv.Call<string>(typeof(GedcomSearch), "getNames", s), Is.EqualTo(names));
            Assert.That(Priv.Call<string>(typeof(GedcomSearch), "getSurname", s), Is.EqualTo(surname));
        }

        [Test]
        public void NomiECognome_Null() {
            Assert.That(Priv.Call(typeof(GedcomSearch), "getNames", (string)null), Is.Null);
            Assert.That(Priv.Call(typeof(GedcomSearch), "getSurname", (string)null), Is.Null);
        }

        // ---------- riferimento: comportamento identico al codice prima dello spostamento ----------

        /// <summary>
        /// Fixtures\ricerca.golden.json: ricerche e schede su ricerca.ged con tutte le 32 combinazioni
        /// delle opzioni, prodotte dal codice che stava in Viewer (verificate identiche, 0 differenze su
        /// 1888 voci, il 20/10/2026). Chiave: "S|bit|ricerca" o "D|bit|id"; bit: FullNames=1, FullDates=2,
        /// ShowSpouse=4, ShowKey=8, ShowAllKeys=16. Righe della ricerca separate da "\n"; null = nessuna scheda.
        /// </summary>
        [Test]
        public void Riferimento_IdenticoAlComportamentoPrecedente() {
            var gold = JsonConvert.DeserializeObject<Dictionary<string, string>>(
                File.ReadAllText(Path.Combine(Sandbox.FixturesDir, "ricerca.golden.json")));
            Assert.That(gold.Count, Is.EqualTo(1888));
            var diffs = new List<string>();
            foreach (var kv in gold) {
                var p = kv.Key.Split(new[] { '|' }, 3);
                int b = int.Parse(p[1]);
                var o = new DescriptionOptions {
                    FullNames = (b & 1) != 0, FullDates = (b & 2) != 0, ShowSpouse = (b & 4) != 0, ShowKey = (b & 8) != 0, ShowAllKeys = (b & 16) != 0,
                };
                string actual = p[0] == "S" ? string.Join("\n", g.Search(p[2], o)) : g.Describe(p[2], o);
                if (actual != kv.Value)
                    diffs.Add(kv.Key);
            }
            Assert.That(diffs, Is.Empty, "voci diverse dal riferimento: " + string.Join(", ", diffs.Take(10)));
        }
    }
}
