using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace viewer.Tests {

    /// <summary>
    /// Logica "pura" della form principale: nomi file, URL IIIF, date, appunti, navigazione nodi.
    /// La Viewer e' creata senza costruttore (vedi Priv.NewViewer).
    /// </summary>
    [TestFixture, Apartment(System.Threading.ApartmentState.STA)]
    public class ViewerHelpersTests {
        Viewer v;

        [SetUp]
        public void SetUp() {
            Tree.Reset();
            v = Priv.NewViewer();
        }

        // ---------- Nomi file e URL immagini ----------

        [TestCase("LqaVpAR.jpg", "_Lqa_Vp_A_R.jpg")]
        [TestCase("wogd6pm.jpg", "wogd6pm.jpg")]
        [TestCase("5xyVJYW", "5xy_V_J_Y_W")]
        public void ExtendCode_MaiuscoleDistinteSuFileSystemCaseInsensitive(string code, string expected) {
            Assert.That(Priv.Call<string>(v, "extendCode", code), Is.EqualTo(expected));
        }

        [Test]
        public void ExtendCode_CodiciCheDifferisconoSoloPerMaiuscole_FileDiversi() {
            string a = Priv.Call<string>(v, "extendCode", "abC");
            string b = Priv.Call<string>(v, "extendCode", "aBc");
            Assert.That(a.ToLowerInvariant(), Is.Not.EqualTo(b.ToLowerInvariant()));
        }

        [Test]
        public void UrlImmagine_DimensioneZero_Max() {
            Priv.Set(v, "dimension", 0);
            Assert.That(Priv.Call<string>(v, "getImageUrlByCode", "wOgD6pM", false),
                Is.EqualTo("https://iiif-antenati.cultura.gov.it/iiif/2/wOgD6pM/full/max/0/default.jpg"));
        }

        [Test]
        public void UrlImmagine_ConDimensione_LarghezzaFissa() {
            Priv.Set(v, "dimension", 1600);
            Assert.That(Priv.Call<string>(v, "getImageUrlByCode", "wOgD6pM", false),
                Is.EqualTo("https://iiif-antenati.cultura.gov.it/iiif/2/wOgD6pM/full/1600,/0/default.jpg"));
            Assert.That(Priv.Call<string>(v, "getImageUrlByCode", "wOgD6pM", true), Does.Contain("/full/max/"),
                "useMax vince sulla dimensione");
        }

        // ---------- URL abbreviati ----------

        [TestCase("https://bit.ly/abc", true)]
        [TestCase("http://j.mp/abc", true)]
        [TestCase("https://tinyurl.com/abc", true)]
        [TestCase("https://antenati.cultura.gov.it/ark:/12657/an_ua1/x", false)]
        [TestCase("https://example.com/bit.ly/", false)]
        public void IsShortened(string url, bool expected) {
            Assert.That(Priv.Call<bool>(v, "isShortened", url), Is.EqualTo(expected));
        }

        [TestCase("https://antenati.cultura.gov.it/ark:/12657/an_ua17439642/wbOG7kv")]
        [TestCase("http://dl.antenati.san.beniculturali.it/v/x")]
        [TestCase("https://example.com/qualcosa")]
        public void RedirectUrl_UrlNonAbbreviati_InvariatiSenzaRete(string url) {
            Assert.That(Priv.Call<string>(v, "redirectUrl", url), Is.EqualTo(url));
        }

        // ---------- Date e anni ----------

        [Test]
        public void ExtractDate_Valida() {
            var args = new object[] { "12_03_1820", 0, 0, 0 };
            Assert.That(Priv.Call<bool>(v, "extractDate", args), Is.True);
            Assert.That(args.Skip(1), Is.EqualTo(new object[] { 1820, 3, 12 }));
        }

        [TestCase("12-03-1820")]
        [TestCase("1_03_1820")]
        [TestCase("aa_03_1820")]
        [TestCase("12_03_1820x")]
        public void ExtractDate_NonValida(string s) {
            var args = new object[] { s, 0, 0, 0 };
            Assert.That(Priv.Call<bool>(v, "extractDate", args), Is.False);
        }

        [TestCase("12 MAR 1820", "1820")]
        [TestCase("ABT 1790", "1790")]
        [TestCase("1813", "1813")]
        [TestCase("", "")]
        [TestCase(null, "")]
        public void GetYear(string date, string expected) {
            Assert.That(Priv.Call<string>(v, "getYear", date), Is.EqualTo(expected));
        }

        [Test]
        public void ParseFirstDate_UnaSolaCopia_QuellaDelTree() {
            // c'erano due copie (Viewer e RegNode) da tenere allineate; ora "Riordina nomi" del Viewer
            // chiama RegNode.rielaboraNomi (vedi RiordinaNomiTests.ViewerUsaLaStessaRegola)
            var all = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            Assert.That(typeof(Viewer).GetMethod("ParseFirstDate", all), Is.Null);
            Assert.That(typeof(RegNode).GetMethod("ParseFirstDate", all), Is.Not.Null);
        }

        // ---------- Nomi e cognomi (ricerca GEDCOM) ----------

        [TestCase("De", true)]
        [TestCase("di", true)]
        [TestCase("Del", true)]
        [TestCase("Rossi", false)]
        [TestCase("Ann", false)]
        [TestCase("", false)]
        public void IsPrefix(string s, bool expected) {
            Assert.That(Priv.Call<bool>(v, "isPrefix", s), Is.EqualTo(expected));
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
            Assert.That(Priv.Call<string>(v, "getNames", s), Is.EqualTo(names));
            Assert.That(Priv.Call<string>(v, "getSurname", s), Is.EqualTo(surname));
        }

        [Test]
        public void NomiECognome_Null() {
            Assert.That(Priv.Call(v, "getNames", (string)null), Is.Null);
            Assert.That(Priv.Call(v, "getSurname", (string)null), Is.Null);
        }

        // ---------- Appunti (appunti.txt, appuntiCitta.txt, famiglieCitta.txt) ----------

        [Test]
        public void Appunti_SalvaERicarica_ConAndateACapo() {
            string file = Path.Combine(Sandbox.NewDir(), "appunti.txt");
            var dic = new Dictionary<string, string> {
                ["282248"] = "prima riga\r\nseconda riga",
                ["282248#12"] = "solo LF\nfine",
                ["Bari"] = "Rossi, Bianchi",
            };
            Priv.Call(v, "SaveTextDictionary", dic, file);

            var back = new Dictionary<string, string>();
            Priv.Call(v, "loadTextDictionary", file, back);

            Assert.That(back, Is.EquivalentTo(dic));
            Assert.That(File.ReadAllLines(file).Length, Is.EqualTo(3), "un appunto per riga");
        }

        [Test]
        public void Appunti_FileMancante_DizionarioInvariato() {
            var dic = new Dictionary<string, string> { ["x"] = "y" };
            Priv.Call(v, "loadTextDictionary", Path.Combine(Sandbox.Root, "non_esiste.txt"), dic);
            Assert.That(dic.Count, Is.EqualTo(1));
        }

        [Test]
        public void Appunti_Caricamento_SovrascriveChiaviEsistenti() {
            string file = Path.Combine(Sandbox.NewDir(), "a.txt");
            File.WriteAllText(file, "k§nuovo\r\n");
            var dic = new Dictionary<string, string> { ["k"] = "vecchio", ["altro"] = "resta" };
            Priv.Call(v, "loadTextDictionary", file, dic);
            Assert.That(dic["k"], Is.EqualTo("nuovo"));
            Assert.That(dic["altro"], Is.EqualTo("resta"));
        }

        // ---------- Navigazione nodi ----------

        [Test]
        public void AddChild_InserimentoOrdinatoSenzaDuplicati() {
            var root = new TreeNode("root");
            foreach (var t in new[] { "Bari", "altamura", "Canosa", "bari" })
                Priv.Call(v, "addChild", root, t, null);

            Assert.That(root.Nodes.Cast<TreeNode>().Select(n => n.Text), Is.EqualTo(new[] { "altamura", "Bari", "Canosa" }),
                "ordine alfabetico, confronto senza maiuscole");
        }

        [Test]
        public void AddChild_NodoEsistente_AggiornaTag() {
            var root = new TreeNode("root");
            var first = Priv.Call<TreeNode>(v, "addChild", root, "Bari", "uno");
            var again = Priv.Call<TreeNode>(v, "addChild", root, "BARI", "due");
            Assert.That(again, Is.SameAs(first));
            Assert.That(first.Tag, Is.EqualTo("due"));
        }

        [Test]
        public void GetNodeByPath() {
            using (var tv = new TreeView()) {
                var bari = tv.Nodes.Add("Bari");
                var rossi = bari.Nodes.Add("Rossi");
                rossi.Nodes.Add("Giuseppe");

                Assert.That(Priv.Call<TreeNode>(v, "getNodeByPath", tv.Nodes, new List<string> { "bari", "ROSSI" }, null), Is.SameAs(rossi));
                Assert.That(Priv.Call<TreeNode>(v, "getNodeByPath", tv.Nodes, new List<string> { "Bari", "Rossi", "Rossi" }, null), Is.SameAs(rossi),
                    "le ripetizioni finali vengono ignorate");
                Assert.That(Priv.Call(v, "getNodeByPath", tv.Nodes, new List<string> { "Bari", "Verdi" }, null), Is.Null);
            }
        }

        [Test]
        public void GetNodePath_GetCity_GetArchive() {
            Tree.AddArchivio();
            var root = new TreeNode("Archivio di Stato di Bari");
            var leaf = root.Nodes.Add("Stato civile napoleonico").Nodes.Add("Bari").Nodes.Add("1813");

            Assert.That(Priv.Call<string>(v, "getNodePath", leaf), Is.EqualTo("Archivio di Stato di Bari/Stato civile napoleonico/Bari/1813/"));
            Assert.That(Priv.Call<string>(v, "getCity", leaf), Is.EqualTo("Bari"));
            Assert.That(Priv.Call<string>(v, "getArchive", leaf), Is.EqualTo("208"));
            Assert.That(Priv.Call(v, "getCity", root), Is.Null, "sopra il comune non c'e' citta'");
        }

        [Test]
        public void GetNodeAddress_UsaHrefDelNodo() {
            Tree.Bari();
            Tree.AddAnno();
            Tree.AddKind();
            var reg = Tree.AddRegistro();
            var n = new TreeNode("x") { Tag = new RegNode(reg) };

            Assert.That(Priv.Call<string>(v, "getNodeAddress", n), Is.EqualTo(reg.href));
            Assert.That(Priv.Call<string>(v, "getNodeAddress2", n), Is.EqualTo(Registro.getIndexUrl2("282216", "1813", "Nati")));
            Assert.That(Priv.Call(v, "getNodeAddress", new TreeNode("senza tag")), Is.Null);
        }

        [Test]
        public void RifPage_TitoloAccorciato() {
            var t = typeof(Viewer).GetNestedType("RifPage", System.Reflection.BindingFlags.NonPublic);
            var rif = Activator.CreateInstance(t, "282248#12", "Archivio/Fondo/Bari/1813/Nati");
            Assert.That(t.GetField("title").GetValue(rif), Is.EqualTo("../Bari/1813/Nati"));

            var shortRif = Activator.CreateInstance(t, "1#1", "Bari/1813");
            Assert.That(t.GetField("title").GetValue(shortRif), Is.EqualTo("Bari/1813"));
        }

        // ---------- registro corrente ----------

        void PrepareSetRegister() {
            Priv.Set(v, "txtCodiceRegistro", new TextBox());
            Priv.Set(v, "txtTotPages", new TextBox { Text = "120" });
            Priv.Set(v, "failCount", new Dictionary<string, int> { ["x"] = 3 });
            Priv.Set(v, "idRegistro", "282248");
            Priv.Set(v, "mapPage", new Dictionary<int, string> { [1] = "a" });
        }

        [Test]
        public void SetRegister_CodiceSconosciuto_VistaVuotaSenzaErrore() {
            PrepareSetRegister();

            Assert.That(() => Priv.Call(v, "setRegister", "999999999"), Throws.Nothing);
            Assert.That(Priv.Get(v, "idRegistro"), Is.Null);
            Assert.That((Dictionary<int, string>)Priv.Get(v, "mapPage"), Is.Empty);
            Assert.That(((TextBox)Priv.Get(v, "txtTotPages")).Text, Is.EqualTo("0"));
            Assert.That((Dictionary<string, int>)Priv.Get(v, "failCount"), Is.Empty);
        }

        [TestCase(null)]
        [TestCase("")]
        public void SetRegister_Vuoto_VistaVuota(string id) {
            PrepareSetRegister();
            Priv.Call(v, "setRegister", id);
            Assert.That(Priv.Get(v, "idRegistro"), Is.Null);
            Assert.That((Dictionary<int, string>)Priv.Get(v, "mapPage"), Is.Empty);
        }

        [Test]
        public void ToIntOrDefault() {
            Assert.That(v.toIntOrDefault("12"), Is.EqualTo(12));
            Assert.That(v.toIntOrDefault("x"), Is.EqualTo(1));
            Assert.That(v.toIntOrDefault("", 7), Is.EqualTo(7));
        }
    }

    /// <summary>Filtri immagine (contrasto, scala di grigi, equalizzazione, bordi, soglia)</summary>
    [TestFixture]
    public class ImageFilterTests {

        static Bitmap Solid(int w, int h, Color c) {
            var b = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b)) g.Clear(c);
            return b;
        }

        [TestCase(-5, 0)]
        [TestCase(300, 255)]
        [TestCase(42, 42)]
        public void Clamp(int value, int expected) {
            Assert.That(Viewer.Clamp(value, 0, 255), Is.EqualTo(expected));
        }

        [Test]
        public void ApplyContrast_Zero_Invariato() {
            using (var b = Solid(4, 4, Color.FromArgb(255, 100, 150, 200))) {
                Viewer.ApplyContrast(0, b);
                var p = b.GetPixel(1, 1);
                Assert.That(new[] { p.R, p.G, p.B }, Is.EqualTo(new byte[] { 100, 150, 200 }).Within(1));
            }
        }

        [Test]
        public void ApplyContrast_Positivo_AllontanaDalGrigioMedio() {
            using (var dark = Solid(2, 2, Color.FromArgb(255, 80, 80, 80)))
            using (var light = Solid(2, 2, Color.FromArgb(255, 180, 180, 180))) {
                Viewer.ApplyContrast(50, dark);
                Viewer.ApplyContrast(50, light);
                Assert.That(dark.GetPixel(0, 0).R, Is.LessThan(80));
                Assert.That(light.GetPixel(0, 0).R, Is.GreaterThan(180));
            }
        }

        [Test]
        public void IncreaseContrast_SaturaAgliEstremi() {
            using (var b = Solid(2, 2, Color.FromArgb(255, 10, 128, 250)))
            using (var r = Viewer.IncreaseContrast(b, 100)) {
                var p = r.GetPixel(0, 0);
                Assert.That(p.R, Is.EqualTo(0));
                Assert.That(p.B, Is.EqualTo(255));
                Assert.That(r.Size, Is.EqualTo(b.Size));
            }
        }

        [Test]
        public void ConvertToGrayscale_CanaliUguali() {
            using (var b = Solid(3, 3, Color.FromArgb(255, 200, 50, 10)))
            using (var g = Priv.Call<Bitmap>(typeof(Viewer), "ConvertToGrayscale", b)) {
                var p = g.GetPixel(1, 1);
                Assert.That(p.R, Is.EqualTo(p.G).And.EqualTo(p.B));
                Assert.That((int)p.R, Is.EqualTo((int)(0.3 * 200 + 0.59 * 50 + 0.11 * 10)).Within(2));
            }
        }

        [Test]
        public void EqualizeHistogram_DueLivelli_DiventanoNeroPienoEBianco() {
            using (var b = new Bitmap(2, 1)) {
                b.SetPixel(0, 0, Color.FromArgb(100, 100, 100));
                b.SetPixel(1, 0, Color.FromArgb(120, 120, 120));
                using (var e = Viewer.EqualizeHistogram(b)) {
                    Assert.That(e.GetPixel(0, 0).R, Is.EqualTo(127));
                    Assert.That(e.GetPixel(1, 0).R, Is.EqualTo(255));
                }
            }
        }

        [Test]
        public void EdgeDetection_ImmagineUniforme_NessunBordo() {
            using (var b = Solid(5, 5, Color.Gray))
            using (var e = Viewer.ApplyEdgeDetection(b)) {
                Assert.That(e.GetPixel(2, 2).R, Is.EqualTo(0));
            }
        }

        [Test]
        public void EdgeDetection_SaltoNetto_BordoMassimo() {
            using (var b = Solid(6, 6, Color.Black)) {
                for (int y = 0; y < 6; y++) for (int x = 3; x < 6; x++) b.SetPixel(x, y, Color.White);
                using (var e = Viewer.ApplyEdgeDetection(b)) {
                    Assert.That(e.GetPixel(3, 3).R, Is.EqualTo(255));
                    Assert.That(e.GetPixel(1, 3).R, Is.EqualTo(0));
                }
            }
        }

        [Test]
        public void Soglia_BiancoONero() {
            using (var b = new Bitmap(2, 1, System.Drawing.Imaging.PixelFormat.Format32bppArgb)) {
                b.SetPixel(0, 0, Color.FromArgb(255, 90, 90, 90));
                b.SetPixel(1, 0, Color.FromArgb(255, 160, 160, 160));
                Priv.Call(typeof(Viewer), "processImage", b, 128);
                Assert.That(b.GetPixel(0, 0).ToArgb(), Is.EqualTo(Color.Black.ToArgb()));
                Assert.That(b.GetPixel(1, 0).ToArgb(), Is.EqualTo(Color.White.ToArgb()));
            }
        }
    }
}
