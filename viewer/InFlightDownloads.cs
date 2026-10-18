using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace viewer {

    /// <summary>
    /// Download di immagini in corso, uno per chiave (codice immagine).
    /// - Chi chiede un'immagine gia' in download riaggancia quel download e ne aspetta il risultato
    ///   (prima riceveva subito il percorso di un file non ancora scritto: LoadBitmapUnlocked
    ///   falliva e il timer ritentava ogni 200 ms finche' il download non finiva).
    /// - Ogni download ha il suo CancellationToken: Cancel/CancelAllExcept lo annullano e la
    ///   chiave si libera subito, cosi' una nuova richiesta parte da capo invece di riagganciare
    ///   quello annullato.
    /// Thread-safe.
    /// </summary>
    public class InFlightDownloads {

        sealed class Entry {
            public Task<string> Task;
            public CancellationTokenSource Cts;
        }

        readonly object sync = new object();
        readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();

        /// <summary>
        /// Download in corso per key, oppure ne avvia uno con start(token).
        /// Il risultato e' quello di start (percorso del file, o null se fallito/annullato).
        /// </summary>
        public Task<string> GetOrStart(string key, Func<CancellationToken, Task<string>> start) {
            Entry e;
            TaskCompletionSource<string> result;
            lock (sync) {
                if (entries.TryGetValue(key, out e))
                    return e.Task;
                // registrata PRIMA di avviare il download (che puo' finire subito); continuation
                // asincrone, cosi' chi aspetta non gira dentro il thread che completa il download
                result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                e = new Entry { Cts = new CancellationTokenSource(), Task = result.Task };
                entries[key] = e;
            }
            // il download parte qui, fuori dal lock, nel thread di chi lo chiede
            Task<string> download;
            try { download = start(e.Cts.Token) ?? Task.FromResult<string>(null); }
            catch (Exception ex) { download = Task.FromException<string>(ex); }
            download.ContinueWith(t => {
                remove(key, e);
                if (t.IsFaulted) result.TrySetException(t.Exception.InnerExceptions);
                else if (t.IsCanceled) result.TrySetResult(null);
                else result.TrySetResult(t.Result);
            }, TaskScheduler.Default);
            return e.Task;
        }

        void remove(string key, Entry e) {
            lock (sync) {
                // solo se e' ancora quello registrato: dopo un Cancel la chiave puo' avere gia' un download nuovo
                if (entries.TryGetValue(key, out Entry current) && current == e)
                    entries.Remove(key);
            }
        }

        public bool Cancel(string key) {
            Entry e;
            lock (sync) {
                if (!entries.TryGetValue(key, out e))
                    return false;
                entries.Remove(key);
            }
            cancel(e);
            return true;
        }

        /// <summary>Annulla tutti i download tranne quelli per cui keep(chiave) e' true; restituisce quanti</summary>
        public int CancelAllExcept(Func<string, bool> keep) {
            List<Entry> victims;
            lock (sync) {
                var keys = entries.Keys.Where(k => !keep(k)).ToList();
                victims = keys.Select(k => entries[k]).ToList();
                foreach (var k in keys)
                    entries.Remove(k);
            }
            foreach (var e in victims)
                cancel(e);
            return victims.Count;
        }

        static void cancel(Entry e) {
            try { e.Cts.Cancel(); }
            catch (ObjectDisposedException) { /* gia' finito */ }
        }

        public bool IsInFlight(string key) {
            lock (sync) { return entries.ContainsKey(key); }
        }

        public int Count {
            get { lock (sync) { return entries.Count; } }
        }
    }
}
