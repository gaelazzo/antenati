using System;
using System.Collections.Generic;

namespace viewer {

    /// <summary>
    /// Ultimi download di immagini del programma (tempi, esito), per la "Diagnosi tempi".
    /// Non fa richieste: registra quello che queueDownloadFile ha gia' misurato. Thread-safe.
    /// </summary>
    public class DownloadStats {

        public class Entry {
            public DateTime When;
            public string Code;
            /// <summary>codice HTTP; 0 = nessuna risposta (timeout, rete, eccezione)</summary>
            public int Status;
            public long Coda;
            public long Server;
            public bool Cancelled;
        }

        readonly object sync = new object();
        readonly Queue<Entry> entries = new Queue<Entry>();
        readonly int capacity;

        public DownloadStats(int capacity = 20) {
            this.capacity = Math.Max(1, capacity);
        }

        public void Add(string code, int status, long coda, long server) {
            add(new Entry { When = DateTime.Now, Code = code, Status = status, Coda = coda, Server = server });
        }

        public void AddCancelled(string code) {
            add(new Entry { When = DateTime.Now, Code = code, Cancelled = true });
        }

        void add(Entry e) {
            lock (sync) {
                entries.Enqueue(e);
                while (entries.Count > capacity)
                    entries.Dequeue();
            }
        }

        /// <summary>Dal piu' vecchio al piu' recente</summary>
        public Entry[] Snapshot() {
            lock (sync) { return entries.ToArray(); }
        }
    }
}
