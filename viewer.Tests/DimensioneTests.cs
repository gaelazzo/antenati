using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace viewer.Tests {

    /// <summary>
    /// Campo "dimensione" (larghezza delle immagini richieste). Le immagini in cache sono salvate
    /// per codice, senza larghezza: cambiando dimensione la cache va svuotata.
    /// Prima lo faceva TextChanged a ogni tasto: scrivendo "2000" la cache veniva svuotata 4 volte
    /// (2, 20, 200, 2000) e nel frattempo si chiedevano immagini larghe 2, 20 e 200 pixel.
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA), NonParallelizable]
    public class DimensioneTests {
        Viewer v;
        TextBox box;
        string cached;

        [SetUp]
        public void SetUp() {
            v = Priv.NewViewer();
            box = new TextBox();
            Priv.Set(v, "txtDimensione", box);
            Priv.Set(v, "savedFiles", new HashSet<string>());
            Priv.Set(v, "dimension", 1000);
            box.Text = "1000";
            Priv.Call(v, "wireDimensionBox");

            Directory.CreateDirectory("data");
            string name = "dimtest_" + Guid.NewGuid().ToString("N");
            cached = Path.Combine("data", name + ".jpg");
            File.WriteAllBytes(cached, new byte[20000]);
            ((HashSet<string>)Priv.Get(v, "savedFiles")).Add(name);
        }

        [TearDown]
        public void TearDown() {
            box.Dispose();
            if (File.Exists(cached)) File.Delete(cached);
        }

        int Dimension => (int)Priv.Get(v, "dimension");

        static void Raise(Control c, string onMethod, EventArgs e) =>
            c.GetType().GetMethod(onMethod, BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { e.GetType() }, null)
             .Invoke(c, new object[] { e });

        void Digita(string testo) {
            box.Text = "";
            foreach (char ch in testo) box.Text += ch;
        }

        [Test]
        public void MentreSiScrive_NienteCambia() {
            Digita("2000");
            Assert.That(Dimension, Is.EqualTo(1000), "le immagini non vanno chieste larghe 2, 20, 200 px");
            Assert.That(File.Exists(cached), Is.True, "la cache non va svuotata a ogni tasto");
        }

        [Test]
        public void UscendoDalCampo_ApplicaESvuotaUnaVolta() {
            Digita("2000");
            Raise(box, "OnLeave", EventArgs.Empty);
            Assert.That(Dimension, Is.EqualTo(2000));
            Assert.That(File.Exists(cached), Is.False);
            Assert.That((HashSet<string>)Priv.Get(v, "savedFiles"), Is.Empty);
        }

        [Test]
        public void Invio_Applica() {
            Digita("1500");
            Raise(box, "OnKeyDown", new KeyEventArgs(Keys.Enter));
            Assert.That(Dimension, Is.EqualTo(1500));
            Assert.That(File.Exists(cached), Is.False);
        }

        [Test]
        public void StessoValore_CacheIntatta() {
            Digita("1000");
            Raise(box, "OnLeave", EventArgs.Empty);
            Assert.That(Dimension, Is.EqualTo(1000));
            Assert.That(File.Exists(cached), Is.True);
        }

        [TestCase("")]
        [TestCase("0")]
        [TestCase("-5")]
        [TestCase("abc")]
        public void ValoreVuotoONonValido_DimensioneNativa(string testo) {
            Digita(testo);
            Raise(box, "OnLeave", EventArgs.Empty);
            Assert.That(Dimension, Is.EqualTo(0), "0 = /full/max/, come prima");
        }

        [Test]
        public void CartellaDataMancante_NessunErrore() {
            Priv.Set(v, "dimension", 1000);
            string saved = Environment.CurrentDirectory;
            Environment.CurrentDirectory = Sandbox.NewDir();
            try {
                Digita("1200");
                Assert.That(() => Raise(box, "OnLeave", EventArgs.Empty), Throws.Nothing);
                Assert.That(Dimension, Is.EqualTo(1200));
            }
            finally { Environment.CurrentDirectory = saved; }
        }
    }
}
