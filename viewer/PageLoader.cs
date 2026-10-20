using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.UI.WebControls;

namespace viewer {

    public static class PageLoader {
		
		public static CookieCollection cookies = new CookieCollection();

        // Ogni richiesta aggiorna i cookie, e le richieste partono insieme da thread diversi
        // (pagina corrente, precaricamento, esplorazione): CookieCollection non e' thread-safe
        static readonly object cookiesLock = new object();

        public static string getCookie(string name) {
            lock (cookiesLock) {
                foreach (Cookie cookie in cookies) {
                  if (cookie.Name==name) return cookie.Value;
                }
            }
            return null;
        }

        /// <summary>
        /// Header di cui i server di dati di Antenati (immagini IIIF, manifest) controllano la provenienza:
        /// senza Referer Cloudflare risponde 403 "Sorry, you have been blocked" (verificato il 20/10/2026).
        /// Sono quelli che la WebView aggiunge alle richieste del browser.
        /// </summary>
        public static Dictionary<string, string> hotlinkHeaders() {
            return new Dictionary<string, string> {
                ["Referer"] = "https://antenati.cultura.gov.it",
                ["Sec-Fetch-Site"] = "same-site",
            };
        }

        /// <summary>
        /// Header di una richiesta ai server di dati (manifest, info.json, immagini) come la manda il
        /// visualizzatore del sito da Chrome: i controlli di provenienza piu' il resto del browser.
        /// Ogni chiamata da' una copia nuova.
        /// </summary>
        public static Dictionary<string, string> siteHeaders() {
            var h = hotlinkHeaders();
            h["Origin"] = "https://antenati.cultura.gov.it";
            h["Accept"] = "*/*";
            h["Accept-Encoding"] = "gzip, deflate";
            h["Accept-Language"] = "it,en-US;q=0.9,en;q=0.8";
            h["Priority"] = "u=1, i";
            h["Sec-Ch-Ua"] = @"""Chromium"";v=""142"", ""Google Chrome"";v=""142"", ""Not?A_Brand"";v=""99""";
            h["Sec-Ch-Ua-Mobile"] = "?0";
            h["Sec-Ch-Ua-Platform"] = "\"Windows\"";
            h["Sec-Fetch-Dest"] = "empty";
            h["Sec-Fetch-Mode"] = "cors";
            return h;
        }

		public static HttpResponseMessage getResponseMessage(string url,
                HttpCompletionOption opt = HttpCompletionOption.ResponseContentRead,
                Dictionary<string, string> headers = null,
                Dictionary<string,string> cookiesDict=null,
                TimeSpan? timeout = null,
                System.Threading.CancellationToken cancel = default) {
			var cookieContainer = new CookieContainer();
			Uri uri = new Uri(url);

			HttpClientHandler handler = new HttpClientHandler() {
				UseCookies = true,
				AllowAutoRedirect = true,
				CookieContainer = cookieContainer,
                AutomaticDecompression =
                            DecompressionMethods.GZip |
                            DecompressionMethods.Deflate  //|DecompressionMethods.Brotli
            };
        
			//handler.CookieContainer.Add(uri, new Cookie("_ga", "GA1.1.1181240017.1762626745"));
			//handler.CookieContainer.Add(uri, new Cookie("_ga_HPLTCJ58MW", "GS2.1.s1762626745$o1$g1$t1762627271$j55$l0$h0"));

			// Aggiungi i cookie attuali alla richiesta
			if (cookiesDict != null) {
				foreach (var kv in cookiesDict)
					cookieContainer.Add(uri, new Cookie(kv.Key, kv.Value ?? ""));
            }

            using (var client = new HttpClient(handler)) {
				// Default di HttpClient: 100 s. Il server IIIF sotto carico puo' metterci di piu'
				// a preparare un'immagine: chi scarica immagini passa un timeout piu' lungo.
				if (timeout.HasValue)
					client.Timeout = timeout.Value;

				// User-Agent obbligatorio
				client.DefaultRequestHeaders.UserAgent.ParseAdd(
					"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36"
				);
				
				if (headers != null) {
                    foreach(string name in headers.Keys) {
                        client.DefaultRequestHeaders.Add(name, headers[name]);
                    }
                }
                try {
                    var response = client.GetAsync(url, opt, cancel).Result;

                    Console.WriteLine(response.Content.Headers.ContentEncoding.FirstOrDefault());

                    // Estrai i cookie per il dominio richiesto

                    var responseCookies = cookieContainer.GetCookies(uri);

                    lock (cookiesLock) {
                        foreach (Cookie cookie in responseCookies) {
                            // Verifica se il cookie esiste già e, in tal caso, aggiornalo
                            var existingCookie = cookies[cookie.Name];
                            if (existingCookie != null && cookie.Value!="") {
                                existingCookie.Value = cookie.Value;
                            }
                            else {
                                cookies.Add(cookie); // Aggiungi il cookie aggiornato
							    Console.WriteLine($"Cookie: {cookie.Name} = {cookie.Value}");

						    }

                        }
                    }

                    //foreach (var head in client.DefaultRequestHeaders) {
                    //    Console.WriteLine($"Header: {head.Key} = {head.Value}");
                    //}
                    //foreach (Cookie cookie in cookies) {
                    //    Console.WriteLine($"Cookie: {cookie.Name} = {cookie.Value}");
                    //}
                    return response;
                }
                catch (Exception ex) {
                    // .Result incapsula l'errore in AggregateException ("Si sono verificati uno o piu' errori"):
                    // scrivo quello vero. TaskCanceledException qui significa timeout scaduto.
                    var inner = (ex as AggregateException)?.GetBaseException() ?? ex;
                    Console.WriteLine(cancel.IsCancellationRequested ? $"Annullata: {url}"
                        : inner is TaskCanceledException ? $"Timeout ({client.Timeout.TotalSeconds:0} s) su {url}"
                        : $"{inner.GetType().Name}: {inner.Message} su {url}");
                    return null;
                }
            }

            //var w = new WebClient();
            //w.Encoding = Encoding.UTF8;
            //var html = w.DownloadString(url);
        }
        static Task warmUpTask;

        /// <summary>
        /// Visita la home di Antenati in background per ottenere PHPSESSID e i cookie del bilanciatore
        /// </summary>
        public static void StartWarmUp() {
            string url = warmUpUrl;
            warmUpTask = Task.Run(() => getResponseMessage(url)?.Dispose());
        }

        // Home di Antenati per il warm-up (modificabile solo dai test, per puntare a un server locale)
        public static string warmUpUrl = "https://antenati.cultura.gov.it/";

        /// <summary>
        /// Attende la fine del warm-up: va chiamato da chi ha bisogno di PHPSESSID
        /// </summary>
        public static void WaitWarmUp() {
            try { warmUpTask?.Wait(); }
            catch { /* senza cookie la richiesta parte lo stesso, come prima in caso di errore */ }
        }

        public static string getPage(string url) {
            var responseBytes = getResponseMessage(url).Content.ReadAsByteArrayAsync().Result;
            return Encoding.UTF8.GetString(responseBytes);
            //return getResponseMessage(url).Content.ReadAsStringAsync().Result;
        }
    }
}
