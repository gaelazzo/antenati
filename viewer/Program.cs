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
            
            // Rilascia il mutex all'uscita
            
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Viewer());
            GC.KeepAlive(mutex);

        }
    }
}
