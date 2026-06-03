using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace viewer {
    static class Program {
        static Mutex mutex = null;

        // Registra su crash.log le eccezioni che finora chiudevano il programma senza alcun
        // avviso (thread di background, task non osservati, AccessViolation/GDI, thread UI).
        static void LogCrash(string source, Exception ex) {
            try {
                string text = $"==== {DateTime.Now:yyyy-MM-dd HH:mm:ss} [{source}] ===={Environment.NewLine}" +
                              $"{ex}{Environment.NewLine}{Environment.NewLine}";
                File.AppendAllText("crash.log", text);
            }
            catch { /* il logging non deve mai propagare a sua volta */ }
        }

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

            // Handler globali di diagnostica: catturano cio' che prima terminava il processo in
            // silenzio. Vanno installati prima di creare qualsiasi finestra.
            AppDomain.CurrentDomain.UnhandledException += (s, e) => {
                LogCrash("AppDomain", e.ExceptionObject as Exception
                                      ?? new Exception(e.ExceptionObject?.ToString() ?? "unknown"));
            };
            TaskScheduler.UnobservedTaskException += (s, e) => {
                LogCrash("UnobservedTask", e.Exception);
                e.SetObserved();
            };
            Application.ThreadException += (s, e) => {
                LogCrash("UIThread", e.Exception);
                MessageBox.Show(e.Exception.ToString(), "Errore non gestito");
            };
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            // Esegui il programma

            // Evita ThreadPool starvation: il codice usa molto .Result/.Wait (sync-over-async),
            // che blocca thread di pool mentre le continuation interne di GetAsync ne richiedono
            // altri. Senza thread liberi il pool cresce ~1-2/s -> le GET restano in stallo per
            // secondi. Pre-allocando i thread minimi il problema sparisce.
            ThreadPool.SetMinThreads(10, 10);

            // Rilascia il mutex all'uscita

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
			{
                Application.Run(new Viewer());
            }
            catch (Exception e)
			{
                LogCrash("Main", e);
                MessageBox.Show(e.ToString(), "Error");

            }
            
            GC.KeepAlive(mutex);

        }
    }
}
