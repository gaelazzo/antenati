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
        public static string getCookie(string name) {
            foreach (Cookie cookie in cookies) {
              if (cookie.Name==name) return cookie.Value;
            }
            return null;
        }

		public static HttpResponseMessage getResponseMessage(string url,
                HttpCompletionOption opt = HttpCompletionOption.ResponseContentRead,
                Dictionary<string, string> headers = null,
                Dictionary<string,string> cookiesDict=null) {
			var cookieContainer = new CookieContainer();
			Uri uri = new Uri(url);

			HttpClientHandler handler = new HttpClientHandler() {
				UseCookies = true,
				AllowAutoRedirect = true,
				CookieContainer = cookieContainer
			};
			//handler.CookieContainer.Add(uri, new Cookie("_ga", "GA1.1.1181240017.1762626745"));
			//handler.CookieContainer.Add(uri, new Cookie("_ga_HPLTCJ58MW", "GS2.1.s1762626745$o1$g1$t1762627271$j55$l0$h0"));

			// Aggiungi i cookie attuali alla richiesta
			if (cookiesDict != null) {
				foreach (var kv in cookiesDict)
					cookieContainer.Add(uri, new Cookie(kv.Key, kv.Value ?? ""));
            }

            using (var client = new HttpClient(handler)) {
				
				// User-Agent obbligatorio
				client.DefaultRequestHeaders.UserAgent.ParseAdd(
					"Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
					"AppleWebKit/537.36 (KHTML, like Gecko) " +
					"Chrome/142.0.0.0 Safari/537.36"
				);
				
				if (headers != null) {
                    foreach(string name in headers.Keys) {
                        client.DefaultRequestHeaders.Add(name, headers[name]);
                    }
                }
                try {
                    var response = client.GetAsync(url,opt).Result;

                    // Estrai i cookie per il dominio richiesto
                   
                    var responseCookies = cookieContainer.GetCookies(uri);

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

                    //foreach (var head in client.DefaultRequestHeaders) {
                    //    Console.WriteLine($"Header: {head.Key} = {head.Value}");
                    //}
                    //foreach (Cookie cookie in cookies) {
                    //    Console.WriteLine($"Cookie: {cookie.Name} = {cookie.Value}");
                    //}
                    return response;
                }
                catch (Exception ex) {
                    Console.WriteLine(ex.Message);
                    return null;
                }
            }

            //var w = new WebClient();
            //w.Encoding = Encoding.UTF8;
            //var html = w.DownloadString(url);
        }
        public static string getPage(string url) {
            var responseBytes = getResponseMessage(url).Content.ReadAsByteArrayAsync().Result;
            return Encoding.UTF8.GetString(responseBytes);
            //return getResponseMessage(url).Content.ReadAsStringAsync().Result;
        }
    }
}
