using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace viewer {

    /// <summary>
    /// "Diagnosi tempi": poche richieste (circa 8) per capire se la lentezza e' del sito o del programma.
    /// 1. home del sito  2. manifest del registro  3. un'immagine gia' vista (pagina corrente)
    /// 4. due pagine mai scaricate, nello stesso momento: una come la scarica il programma (full/W,),
    ///    l'altra come il visualizzatore del sito (info.json + tasselli 1024 a scala 2)
    /// + riepilogo degli ultimi download del programma (DownloadStats, nessuna richiesta in piu').
    /// </summary>
    public static class ServerProbe {

        public class Options {
            public string SiteUrl = AntenatiSite.root;
            public string ManifestUrl;
            public IiifServer Iiif = IiifServer.current;
            /// <summary>larghezza richiesta dal programma (0 = full/max)</summary>
            public int Width;
            /// <summary>codice di un'immagine gia' vista (pagina corrente), null per saltare</summary>
            public string ViewedCode;
            /// <summary>codici di pagine mai scaricate: [0] modo del programma, [1] modo del sito</summary>
            public string[] ColdCodes = new string[0];
            /// <summary>sotto: "veloce"; sotto SlowMs: "lento"; sopra: "molto lento"</summary>
            public int FastMs = 3000;
            public int SlowMs = 20000;
            public TimeSpan Timeout = TimeSpan.FromSeconds(150);
        }

        public class Step {
            public string Name;
            public long Ms;
            /// <summary>codice HTTP; 0 = nessuna risposta</summary>
            public int Status;
            public string Detail = "";
            public bool Ok => Status >= 200 && Status < 300;
        }

        public class Report {
            public List<Step> Steps = new List<Step>();
            public string Verdict;
            public bool TilesFaster;
            public string Text;
        }

        static Step Get(string name, string url, Options o, bool readBody, out string body) {
            body = null;
            var sw = Stopwatch.StartNew();
            using (var r = PageLoader.getResponseMessage(url, HttpCompletionOption.ResponseContentRead, PageLoader.siteHeaders(), null, o.Timeout)) {
                var step = new Step { Name = name, Ms = sw.ElapsedMilliseconds, Status = r == null ? 0 : (int)r.StatusCode };
                if (readBody && r != null && r.IsSuccessStatusCode)
                    body = r.Content.ReadAsStringAsync().Result;
                return step;
            }
        }

        static Step Get(string name, string url, Options o) => Get(name, url, o, false, out _);

        /// <summary>Pagina come la scarica il visualizzatore del sito: info.json poi i tasselli in parallelo</summary>
        static Step SiteLike(string code, Options o) {
            var sw = Stopwatch.StartNew();
            var info = Get("info", o.Iiif.infoUrl(code), o, true, out string json);
            if (!info.Ok || json == null)
                return new Step { Name = "Pagina nuova (tasselli, come il sito)", Ms = sw.ElapsedMilliseconds, Status = info.Status, Detail = "info.json non disponibile" };
            var i = IiifServer.parseInfo(json);
            var regions = IiifServer.tileRegions(i.width, i.height, i.tileWidth, 2);
            var tiles = regions.Select(rg => Task.Factory.StartNew(() => Get("tassello", o.Iiif.tileUrl(code, rg), o),
                                                                  TaskCreationOptions.LongRunning)).ToArray();
            Task.WaitAll(tiles);
            var bad = tiles.Select(t => t.Result).FirstOrDefault(s => !s.Ok);
            return new Step {
                Name = "Pagina nuova (tasselli, come il sito)",
                Ms = sw.ElapsedMilliseconds,
                Status = bad?.Status ?? 200,
                Detail = $"info.json {Sec(info.Ms)} + {regions.Length} tasselli insieme (il piu' lento {Sec(tiles.Max(t => t.Result.Ms))})",
            };
        }

        static string Sec(long ms) => (ms / 1000.0).ToString("0.0", CultureInfo.GetCultureInfo("it-IT")) + " s";

        public static Report Run(Options o, DownloadStats stats) {
            var rep = new Report();
            rep.Steps.Add(Get("Sito antenati", o.SiteUrl, o));
            if (o.ManifestUrl != null)
                rep.Steps.Add(Get("Manifest del registro", o.ManifestUrl, o));
            Step viewed = null, programma = null, sito = null;
            if (o.ViewedCode != null) {
                viewed = Get("Immagine gia' vista", o.Iiif.imageUrl(o.ViewedCode, o.Width), o);
                rep.Steps.Add(viewed);
            }
            var cold = o.ColdCodes ?? new string[0];
            if (cold.Length > 0) {
                // nello stesso momento, cosi' il confronto non dipende dagli sbalzi del server
                var tA = Task.Factory.StartNew(() => Get("Pagina nuova (modo del programma)", o.Iiif.imageUrl(cold[0], o.Width), o),
                                               TaskCreationOptions.LongRunning);
                var tB = cold.Length > 1 ? Task.Factory.StartNew(() => SiteLike(cold[1], o), TaskCreationOptions.LongRunning) : null;
                programma = tA.Result;
                rep.Steps.Add(programma);
                if (tB != null) {
                    sito = tB.Result;
                    rep.Steps.Add(sito);
                }
            }

            // conclusione
            var verdict = new StringBuilder();
            var images = new[] { viewed, programma }.Where(s => s != null).ToList();
            var okImages = images.Where(s => s.Ok).ToList();
            if (rep.Steps[0].Status == 0) {
                verdict.Append("Sito non raggiungibile: nessuna risposta da antenati (rete assente o sito fermo).");
            }
            else if (images.Count > 0 && okImages.Count == 0) {
                verdict.Append("Il server delle immagini risponde con errori (" +
                               string.Join(", ", images.Select(s => s.Status == 0 ? "nessuna risposta" : "HTTP " + s.Status).Distinct()) +
                               "): il problema e' del sito, non del programma.");
            }
            else if (okImages.Count > 0) {
                long t = okImages.Max(s => s.Ms);
                string sec = Sec(t);
                if (t < o.FastMs) verdict.Append($"Server veloce: {sec} per immagine.");
                else if (t < o.SlowMs) verdict.Append($"Server lento: {sec} per immagine (di norma sotto {Sec(o.FastMs)}). Conviene aumentare la cache e aspettare che il precaricamento lavori.");
                else verdict.Append($"Server molto lento: {sec} per immagine (di norma sotto {Sec(o.FastMs)}): il problema e' del sito, il programma puo' solo aspettare.");
                if (okImages.Count < images.Count || (sito != null && !sito.Ok))
                    verdict.Append(" Alcune richieste sono fallite con errori.");
            }
            if (programma != null && programma.Ok && sito != null && sito.Ok && sito.Ms < programma.Ms * 0.7) {
                rep.TilesFaster = true;
                verdict.Append($" Le pagine a tasselli (come il sito) sono arrivate prima: {Sec(sito.Ms)} contro {Sec(programma.Ms)}.");
            }

            var recent = stats?.Snapshot() ?? new DownloadStats.Entry[0];
            long maxCoda = recent.Where(e => !e.Cancelled).Select(e => e.Coda).DefaultIfEmpty(0).Max();
            if (maxCoda > 2000)
                verdict.Append($" Nel programma alcune richieste sono rimaste in coda fino a {Sec(maxCoda)}.");
            else if (recent.Length > 0)
                verdict.Append(" Il programma non ha richieste in coda.");
            rep.Verdict = verdict.ToString().Trim();

            // testo
            var sb = new StringBuilder();
            sb.AppendLine($"Diagnosi tempi delle {DateTime.Now:HH:mm:ss}");
            sb.AppendLine();
            foreach (var s in rep.Steps)
                sb.AppendLine($"{s.Name,-38} {Sec(s.Ms),8}  {(s.Status == 0 ? "nessuna risposta" : "HTTP " + s.Status)}" + (s.Detail != "" ? "   " + s.Detail : ""));
            sb.AppendLine();
            if (recent.Length == 0) {
                sb.AppendLine("Nessun download registrato in questa sessione.");
            }
            else {
                var done = recent.Where(e => !e.Cancelled).ToList();
                var okSrv = done.Where(e => e.Status >= 200 && e.Status < 300).Select(e => e.Server).OrderBy(x => x).ToList();
                sb.Append($"Ultimi {recent.Length} download: ");
                if (okSrv.Count > 0)
                    sb.Append($"server mediana {Sec(okSrv[(okSrv.Count - 1) / 2])}, max {Sec(okSrv.Last())}; ");
                sb.Append($"coda max {Sec(maxCoda)}");
                foreach (var g in done.Where(e => e.Status == 0 || e.Status >= 300).GroupBy(e => e.Status).OrderBy(g => g.Key))
                    sb.Append(g.Key == 0 ? $"; senza risposta: {g.Count()}" : $"; HTTP {g.Key}: {g.Count()}");
                int canc = recent.Count(e => e.Cancelled);
                if (canc > 0)
                    sb.Append($"; annullati: {canc}");
                sb.AppendLine(".");
            }
            sb.AppendLine();
            sb.AppendLine("Conclusione: " + rep.Verdict);
            rep.Text = sb.ToString();
            return rep;
        }
    }
}
