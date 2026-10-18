using System.Collections.Generic;

namespace viewer {

    /// <summary>
    /// Decide quando timer1_Tick avvia il caricamento della pagina richiesta.
    /// - niente se la pagina e' gia' a video o e' gia' pronta in attesa di disegno (prima veniva
    ///   caricata e disegnata una seconda volta al tick successivo);
    /// - mai due caricamenti della stessa pagina;
    /// - al massimo MaxInFlight caricamenti insieme: un download lento della pagina appena lasciata
    ///   non blocca la nuova (come faceva il flag unico "updating"), ma sfogliando in fretta non
    ///   parte un download per ogni pagina attraversata (come faceva "loadingPage").
    /// Thread-safe: TryBegin gira sul timer (UI), End nel Task di caricamento.
    /// </summary>
    public class PageLoadScheduler {
        readonly object sync = new object();
        readonly HashSet<int> inFlight = new HashSet<int>();

        public int MaxInFlight { get; }

        public PageLoadScheduler(int maxInFlight = 2) {
            MaxInFlight = maxInFlight;
        }

        /// <param name="requested">pagina richiesta (0 = nessuna)</param>
        /// <param name="shown">pagina a video (nEffettivo)</param>
        /// <param name="staged">pagina caricata e in attesa di disegno (0 = nessuna)</param>
        /// <returns>true se il chiamante deve avviare il caricamento e poi chiamare End</returns>
        public bool TryBegin(int requested, int shown, int staged) {
            if (requested == 0 || requested == shown || requested == staged)
                return false;
            lock (sync) {
                if (inFlight.Contains(requested) || inFlight.Count >= MaxInFlight)
                    return false;
                inFlight.Add(requested);
                return true;
            }
        }

        public void End(int page) {
            lock (sync) {
                inFlight.Remove(page);
            }
        }

        public int InFlightCount {
            get { lock (sync) { return inFlight.Count; } }
        }
    }
}
