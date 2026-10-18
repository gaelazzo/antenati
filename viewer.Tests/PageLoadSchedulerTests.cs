using NUnit.Framework;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace viewer.Tests {

    /// <summary>
    /// Quando timer1_Tick avvia il caricamento della pagina richiesta.
    /// Storia: col flag unico "updating" un download lento bloccava ogni altra pagina; col
    /// "loadingPage" che l'ha sostituito ogni pagina attraversata avviava un download mai annullato;
    /// e in entrambi i casi la pagina appena pronta veniva caricata (e disegnata) una seconda volta.
    /// </summary>
    [TestFixture]
    public class PageLoadSchedulerTests {

        [Test]
        public void PaginaNuova_Avviata() {
            var s = new PageLoadScheduler();
            Assert.That(s.TryBegin(requested: 5, shown: 4, staged: 0), Is.True);
            Assert.That(s.InFlightCount, Is.EqualTo(1));
        }

        [Test]
        public void NessunaPaginaRichiesta_NienteDaFare() {
            Assert.That(new PageLoadScheduler().TryBegin(0, 4, 0), Is.False);
        }

        [Test]
        public void PaginaGiaAVideo_NonRicaricata() {
            Assert.That(new PageLoadScheduler().TryBegin(4, 4, 0), Is.False);
        }

        [Test]
        public void PaginaProntaInAttesaDiDisegno_NonRicaricata() {
            // Era il doppio caricamento: il load finiva, e al tick dopo (prima del disegno) ne partiva un altro
            var s = new PageLoadScheduler();
            Assert.That(s.TryBegin(5, 4, 0), Is.True);
            s.End(5);
            Assert.That(s.TryBegin(5, 4, staged: 5), Is.False);
        }

        [Test]
        public void StessaPaginaGiaInCaricamento_NonDuplicata() {
            var s = new PageLoadScheduler();
            Assert.That(s.TryBegin(5, 4, 0), Is.True);
            for (int tick = 0; tick < 50; tick++)
                Assert.That(s.TryBegin(5, 4, 0), Is.False);
            Assert.That(s.InFlightCount, Is.EqualTo(1));
        }

        [Test]
        public void PaginaLasciata_NonBloccaLaNuova() {
            // Era il blocco del flag unico: pagina 5 appesa al server, l'utente passa alla 6
            var s = new PageLoadScheduler();
            Assert.That(s.TryBegin(5, 4, 0), Is.True);
            Assert.That(s.TryBegin(6, 4, 0), Is.True);
        }

        [Test]
        public void SfogliandoVeloce_AlPiuDueCaricamentiInsieme() {
            // Era la valanga di loadingPage: ogni pagina attraversata partiva e restava in volo
            var s = new PageLoadScheduler();
            int avviati = Enumerable.Range(5, 10).Count(p => s.TryBegin(p, 4, 0));
            Assert.That(avviati, Is.EqualTo(2));
            Assert.That(s.InFlightCount, Is.EqualTo(2));
        }

        [Test]
        public void FinitoUnCaricamento_LaPaginaInAttesaParte() {
            var s = new PageLoadScheduler();
            s.TryBegin(5, 4, 0);
            s.TryBegin(6, 4, 0);
            Assert.That(s.TryBegin(7, 4, 0), Is.False);
            s.End(5);
            Assert.That(s.TryBegin(7, 4, 0), Is.True);
        }

        [Test]
        public void PaginaFallita_RichiedibileDiNuovo() {
            // il retry dopo un errore lo decide failCount (timer1_Tick), non il gate
            var s = new PageLoadScheduler();
            s.TryBegin(5, 4, 0);
            s.End(5);
            Assert.That(s.TryBegin(5, 4, 0), Is.True);
        }

        [Test]
        public void EndDiPaginaMaiAvviata_Innocuo() {
            var s = new PageLoadScheduler();
            Assert.That(() => s.End(42), Throws.Nothing);
            Assert.That(s.InFlightCount, Is.EqualTo(0));
        }

        [Test]
        public void TickConcorrenti_UnSoloAvvioPerPagina() {
            var s = new PageLoadScheduler();
            int avviati = 0;
            using (var go = new ManualResetEventSlim(false)) {
                var tasks = Enumerable.Range(0, 32).Select(_ => Task.Run(() => {
                    go.Wait();
                    if (s.TryBegin(9, 4, 0)) Interlocked.Increment(ref avviati);
                })).ToArray();
                go.Set();
                Task.WaitAll(tasks);
            }
            Assert.That(avviati, Is.EqualTo(1));
        }
    }
}
