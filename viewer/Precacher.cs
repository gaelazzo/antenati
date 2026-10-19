using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace viewer {

    /// <summary>
    /// Precaricamento delle pagine successive a quella in lettura.
    /// - fino a maxParallel download insieme, avviati nell'ordine di lettura: il server IIIF li serve
    ///   in parallelo (19/10/2026: 3 pagine nuove 12,7 s una dopo l'altra, 2,8 s insieme);
    /// - un nuovo Start ferma il precedente (che non avvia altre pagine) e parte subito: prima, se un
    ///   precaricamento era in corso (es. del registro appena lasciato), quello nuovo non partiva;
    /// - una pagina fallita ferma il precaricamento, come prima: niente raffica di richieste al server.
    /// load(codice) restituisce il percorso del file, o null se fallito/annullato.
    /// </summary>
    public class Precacher {
        readonly int maxParallel;
        int generation;
        int running;

        public Precacher(int maxParallel = 3) {
            this.maxParallel = Math.Max(1, maxParallel);
        }

        public bool IsRunning => Volatile.Read(ref running) > 0;

        /// <summary>Ultimo codice avviato (per l'indicatore nella finestra), null se fermo</summary>
        public string Current { get; private set; }

        public void Stop() {
            Interlocked.Increment(ref generation);
        }

        public void Start(IList<string> codes, Func<string, Task<string>> load) {
            int gen = Interlocked.Increment(ref generation);
            Interlocked.Increment(ref running);
            Task.Run(() => run(gen, codes, load));
        }

        sealed class RunState {
            public volatile bool failed;
        }

        async Task run(int gen, IList<string> codes, Func<string, Task<string>> load) {
            var state = new RunState();
            var slots = new SemaphoreSlim(maxParallel);
            var inFlight = new List<Task>();
            try {
                foreach (var code in codes) {
                    await slots.WaitAsync().ConfigureAwait(false);
                    if (gen != Volatile.Read(ref generation) || state.failed) {
                        slots.Release();
                        break;
                    }
                    Current = code;
                    inFlight.Add(loadOne(code, load, slots, state));
                }
                await Task.WhenAll(inFlight).ConfigureAwait(false);
            }
            catch (Exception ex) {
                Console.WriteLine("precache error: " + ex.Message);
            }
            finally {
                if (gen == Volatile.Read(ref generation))
                    Current = null;
                Interlocked.Decrement(ref running);
            }
        }

        static async Task loadOne(string code, Func<string, Task<string>> load, SemaphoreSlim slots, RunState state) {
            try {
                // Task.Run: il download del programma blocca il thread fino alla risposta del server
                // (.Result in PageLoader); chiamato qui direttamente, fermava il ciclo che avvia gli altri
                if (await Task.Run(() => load(code)).ConfigureAwait(false) == null)
                    state.failed = true;
            }
            catch (Exception ex) {
                state.failed = true;
                Console.WriteLine($"precache {code}: {ex.Message}");
            }
            finally {
                slots.Release();
            }
        }
    }
}
