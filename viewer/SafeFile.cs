using System;
using System.IO;
using System.Text;

namespace viewer {
    /// <summary>
    /// Scrittura su file ATOMICA e durevole, drop-in replacement di File.WriteAllText.
    ///
    /// File.WriteAllText tronca e riscrive il file "in place": se l'app crasha (o salta
    /// la corrente) a metà scrittura, il file resta corrotto/troncato e al successivo
    /// caricamento la deserializzazione esplode -> dati persi.
    ///
    /// SafeFile.WriteAllText invece:
    ///   1) scrive su un file temporaneo nella STESSA cartella (stesso volume),
    ///      forzando il flush su disco;
    ///   2) sostituisce l'originale con un RENAME atomico (File.Replace), tenendo
    ///      una copia di backup ".bak" dell'ultima versione valida.
    /// In caso di crash, o resta il vecchio file intatto o il nuovo completo: mai un
    /// file a metà.
    /// </summary>
    public static class SafeFile {
        // UTF-8 SENZA BOM, per restare identici al default di File.WriteAllText.
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static void WriteAllText(string path, string contents) {
            WriteAllText(path, contents, Utf8NoBom);
        }

        public static void WriteAllText(string path, string contents, Encoding encoding) {
            string fullPath = Path.GetFullPath(path);
            string dir = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(dir)) dir = Directory.GetCurrentDirectory();

            // Temp univoco nella stessa cartella -> il rename finale è sullo stesso volume (atomico).
            string tmp = Path.Combine(dir, Path.GetFileName(fullPath) + ".tmp" + Guid.NewGuid().ToString("N"));

            try {
                // 1) Scrivi il temporaneo e forza i buffer su disco prima di pubblicarlo.
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None,
                                               4096, FileOptions.WriteThrough))
                using (var writer = new StreamWriter(fs, encoding)) {
                    writer.Write(contents);
                    writer.Flush();
                    fs.Flush(true); // FlushFileBuffers: durevole anche a corrente staccata
                }

                // 2) Pubblica con rename atomico.
                if (File.Exists(fullPath)) {
                    string backup = fullPath + ".bak";
                    // File.Replace è atomico e conserva la versione precedente in .bak.
                    File.Replace(tmp, fullPath, backup, ignoreMetadataErrors: true);
                }
                else {
                    File.Move(tmp, fullPath);
                }
            }
            catch {
                // Se qualcosa va storto, l'originale resta intatto: ripulisci solo il temp.
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
                throw;
            }
        }
    }
}
