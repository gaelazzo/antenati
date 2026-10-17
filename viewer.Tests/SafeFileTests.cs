using NUnit.Framework;
using System.IO;
using System.Linq;
using System.Text;

namespace viewer.Tests {

    [TestFixture]
    public class SafeFileTests {
        string dir;

        [SetUp]
        public void SetUp() { dir = Sandbox.NewDir(); }

        [Test]
        public void NuovoFile_ScrittoUtf8SenzaBom() {
            string path = Path.Combine(dir, "a.json");
            SafeFile.WriteAllText(path, "città");

            byte[] bytes = File.ReadAllBytes(path);
            Assert.That(bytes, Is.EqualTo(new UTF8Encoding(false).GetBytes("città")));
            Assert.That(File.Exists(path + ".bak"), Is.False, "al primo salvataggio non c'e' niente da salvare in .bak");
        }

        [Test]
        public void Sovrascrittura_TieneVersionePrecedenteInBak() {
            string path = Path.Combine(dir, "a.json");
            SafeFile.WriteAllText(path, "v1");
            SafeFile.WriteAllText(path, "v2");
            SafeFile.WriteAllText(path, "v3");

            Assert.That(File.ReadAllText(path), Is.EqualTo("v3"));
            Assert.That(File.ReadAllText(path + ".bak"), Is.EqualTo("v2"));
        }

        [Test]
        public void NonLasciaFileTemporanei() {
            string path = Path.Combine(dir, "a.json");
            SafeFile.WriteAllText(path, "v1");
            SafeFile.WriteAllText(path, "v2");

            var files = Directory.GetFiles(dir).Select(Path.GetFileName).OrderBy(x => x);
            Assert.That(files, Is.EqualTo(new[] { "a.json", "a.json.bak" }));
        }

        [Test]
        public void EncodingEsplicito_VieneRispettato() {
            string path = Path.Combine(dir, "bom.txt");
            SafeFile.WriteAllText(path, "x", new UTF8Encoding(true));

            byte[] bytes = File.ReadAllBytes(path);
            Assert.That(bytes.Take(3), Is.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }));
        }

        [Test]
        public void PercorsoRelativo_RisoltoSullaCartellaCorrente() {
            string name = "rel_" + System.Guid.NewGuid().ToString("N") + ".txt";
            SafeFile.WriteAllText(name, "ok");
            try {
                Assert.That(File.ReadAllText(Path.Combine(Sandbox.Root, name)), Is.EqualTo("ok"));
            }
            finally { File.Delete(Path.Combine(Sandbox.Root, name)); }
        }

        [Test]
        public void ErroreDiScrittura_LasciaIntattoOriginale() {
            string path = Path.Combine(dir, "a.json");
            SafeFile.WriteAllText(path, "buono");

            // File di destinazione bloccato in esclusiva: la Replace fallisce
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) {
                Assert.That(() => SafeFile.WriteAllText(path, "nuovo"), Throws.InstanceOf<IOException>());
            }

            Assert.That(File.ReadAllText(path), Is.EqualTo("buono"));
            Assert.That(Directory.GetFiles(dir, "*.tmp*"), Is.Empty, "il temporaneo va ripulito");
        }

        [Test]
        public void CartellaInesistente_Eccezione() {
            string path = Path.Combine(dir, "manca", "a.json");
            Assert.That(() => SafeFile.WriteAllText(path, "x"), Throws.InstanceOf<DirectoryNotFoundException>());
        }
    }
}
