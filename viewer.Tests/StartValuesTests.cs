using NUnit.Framework;
using System.Threading;
using System.Windows.Forms;

namespace viewer.Tests {

    /// <summary>
    /// Ripristino dello zoom da start.txt (riga "zoom§N", N = valore di trackZoom, 50..5000).
    /// Prima: userZoom = N / 100 (altrove e' sempre N / 1000) e nessun controllo dell'intervallo.
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA)]
    public class StartValuesTests {
        Viewer v;
        TrackBar track;

        [SetUp]
        public void SetUp() {
            v = Priv.NewViewer();
            // come nel designer: 50..5000, valore iniziale 500
            track = new TrackBar { Minimum = 50, Maximum = 5000, Value = 500 };
            Priv.Set(v, "trackZoom", track);
            Priv.Set(v, "userZoom", 1.0f);
        }

        [TearDown]
        public void TearDown() { track.Dispose(); }

        void Apply(string value) => Priv.Call(v, "applyZoomSetting", value);
        float UserZoom => (float)Priv.Get(v, "userZoom");

        [Test]
        public void ZoomSalvato_StessaScalaDellaBarra() {
            Apply("950");
            Assert.That(track.Value, Is.EqualTo(950));
            Assert.That(UserZoom, Is.EqualTo(0.95f).Within(1e-6));
        }

        [Test]
        public void ZoomSalvatoUgualeAlDefault_NonDieciVolteTroppoGrande() {
            // 500 = valore iniziale della barra: ValueChanged non scatta e restava userZoom = 5.0
            Apply("500");
            Assert.That(UserZoom, Is.EqualTo(0.5f).Within(1e-6));
        }

        [TestCase("99999", 5000)]
        [TestCase("1", 50)]
        public void ZoomFuoriIntervallo_RiportatoAiLimiti(string saved, int expected) {
            // prima: ArgumentOutOfRangeException, che in readStartValues saltava tutte le righe dopo
            Assert.That(() => Apply(saved), Throws.Nothing);
            Assert.That(track.Value, Is.EqualTo(expected));
            Assert.That(UserZoom, Is.EqualTo(expected / 1000f).Within(1e-6));
        }

        [TestCase("")]
        [TestCase("abc")]
        public void ZoomIllegibile_Ignorato(string saved) {
            Assert.That(() => Apply(saved), Throws.Nothing);
            Assert.That(track.Value, Is.EqualTo(500));
            Assert.That(UserZoom, Is.EqualTo(1.0f), "resta il valore che c'era");
        }
    }
}
