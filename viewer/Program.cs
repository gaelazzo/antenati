using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace viewer {
    static class Program {
        static Mutex mutex = null;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main() {
            string mutexName = "ViewerHGLMutex";

            // Creare il mutex
            bool isNewInstance;
            mutex = new Mutex(true, mutexName, out isNewInstance);

            if (!isNewInstance) {
                MessageBox.Show("Un'altra istanza del programma è già in esecuzione.",
                                "Errore",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
                return;
            }

            // Esegui il programma

            // Evita ThreadPool starvation: il codice usa molto .Result/.Wait (sync-over-async),
            // che blocca thread di pool mentre le continuation interne di GetAsync ne richiedono
            // altri. Senza thread liberi il pool cresce ~1-2/s -> le GET restano in stallo per
            // secondi. Pre-allocando i thread minimi il problema sparisce.
            ThreadPool.SetMinThreads(100, 100);

            // Rilascia il mutex all'uscita

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
			{
                Application.Run(new Viewer());
            }
            catch (Exception e)
			{
                MessageBox.Show(e.ToString(), "Error");

            }
            
            GC.KeepAlive(mutex);

        }
    }
}
