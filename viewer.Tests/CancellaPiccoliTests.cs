using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace viewer.Tests {

    /// <summary>
    /// Pulsante "Cancella piccoli": toglie dalla cache data\ le immagini troppo piccole.
    /// Prima: soglia fissa di 1000 px su larghezza E altezza (con dimensione 800 cancellava tutto,
    /// con 1000 cancellava le doppie pagine orizzontali), crash su file corrotto, stop su file bloccato.
    /// </summary>
    [TestFixture]
    public class CancellaPiccoliTests {
        Viewer v;
        string dir;

        [SetUp]
        public void SetUp() {
            v = Priv.NewViewer();
            dir = Sandbox.NewDir();
            Priv.Set(v, "savedFiles", new HashSet<string>());
        }

        string Put(string name, int w, int h) {
            // BMP con estensione .jpg: decodificabile da GDI+ e sempre oltre i 10.000 byte
            string p = Path.Combine(dir, name + ".jpg");
            using (var b = new Bitmap(w, h)) b.Save(p, ImageFormat.Bmp);
            ((HashSet<string>)Priv.Get(v, "savedFiles")).Add(name);
            return p;
        }

        int Run(int dimension) {
            Priv.Set(v, "dimension", dimension);
            return Priv.Call<int>(v, "deleteSmallImages", dir);
        }

        HashSet<string> Saved => (HashSet<string>)Priv.Get(v, "savedFiles");

        [Test]
        public void Dimensione800_ImmaginiA800Conservate() {
            string ok = Put("ok", 800, 1100), piccola = Put("piccola", 400, 600);
            Assert.That(Run(800), Is.EqualTo(1));
            Assert.That(File.Exists(ok), Is.True, "prima: cancellata perche' sotto 1000");
            Assert.That(File.Exists(piccola), Is.False);
            Assert.That(Saved, Does.Contain("ok").And.Not.Contain("piccola"));
        }

        [Test]
        public void Dimensione1000_DoppiaPaginaOrizzontaleConservata() {
            string doppia = Put("doppia", 1000, 760);
            Assert.That(Run(1000), Is.EqualTo(0));
            Assert.That(File.Exists(doppia), Is.True, "prima: cancellata per l'altezza sotto 1000");
        }

        [Test]
        public void DimensioneNativa_SogliaMille() {
            string bassa = Put("bassa", 900, 1300), alta = Put("alta", 2400, 1800);
            Assert.That(Run(0), Is.EqualTo(1));
            Assert.That(File.Exists(bassa), Is.False);
            Assert.That(File.Exists(alta), Is.True);
        }

        [Test]
        public void Segnaposto_Cancellato() {
            string s = Put("segnaposto", 1200, 90);
            Assert.That(Run(1000), Is.EqualTo(1));
            Assert.That(File.Exists(s), Is.False);
        }

        [Test]
        public void FileMinuscolo_Cancellato() {
            string p = Path.Combine(dir, "mini.jpg");
            File.WriteAllBytes(p, new byte[500]);
            Assert.That(Run(1000), Is.EqualTo(1));
            Assert.That(File.Exists(p), Is.False);
        }

        [Test]
        public void FileCorrotto_CancellatoSenzaFermareLaScansione() {
            string rotto = Path.Combine(dir, "rotto.jpg");
            File.WriteAllBytes(rotto, Enumerable.Repeat((byte)0x5A, 50000).ToArray());
            string piccola = Put("piccola", 300, 300), buona = Put("buona", 1500, 2000);

            Assert.That(() => Run(1000), Throws.Nothing);
            Assert.That(File.Exists(rotto), Is.False, "un file illeggibile in cache non verrebbe mai riscaricato");
            Assert.That(File.Exists(piccola), Is.False);
            Assert.That(File.Exists(buona), Is.True);
        }

        [Test]
        public void FileBloccato_SaltatoGliAltriElaborati() {
            string bloccata = Put("bloccata", 300, 300), piccola = Put("piccola", 300, 300);
            using (new FileStream(bloccata, FileMode.Open, FileAccess.Read, FileShare.None)) {
                Assert.That(() => Run(1000), Throws.Nothing);
            }
            Assert.That(File.Exists(piccola), Is.False);
            Assert.That(File.Exists(bloccata), Is.True, "in uso da un altro thread: si ritentera'");
        }

        [Test]
        public void CartellaMancante_Zero() {
            Assert.That(Priv.Call<int>(v, "deleteSmallImages", Path.Combine(dir, "non-esiste")), Is.EqualTo(0));
        }
    }
}
