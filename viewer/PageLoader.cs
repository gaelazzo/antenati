using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net;
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
            var handler = new HttpClientHandler {
                UseCookies = true,
                CookieContainer = new CookieContainer()  // CookieContainer per raccogliere i cookie
            };
            // Aggiungi i cookie attuali alla richiesta
            if (cookiesDict != null) {
                Uri uri = new Uri(url);
                foreach (string name in cookiesDict.Keys) {
                    handler.CookieContainer.Add(uri, new Cookie(name, cookiesDict[name]??""));
                }
            }

            using (var client = new HttpClient(handler)) {
                if (headers != null) {
                    foreach(string name in headers.Keys) {
                        client.DefaultRequestHeaders.Add(name, headers[name]);
                    }
                }
                try {
                    var response = client.GetAsync(url,opt).Result;

                    // Estrai i cookie per il dominio richiesto
                    Uri uri = new Uri(url);
                    var responseCookies = handler.CookieContainer.GetCookies(uri);

                    foreach (Cookie cookie in responseCookies) {
                        // Verifica se il cookie esiste già e, in tal caso, aggiornalo
                        var existingCookie = cookies[cookie.Name];
                        if (existingCookie != null) {
                            existingCookie.Value = cookie.Value;
                        }
                        else {
                            cookies.Add(cookie); // Aggiungi il cookie aggiornato
                        }

                    }

                    foreach (var head in client.DefaultRequestHeaders) {
                       // Console.WriteLine($"Header: {head.Key} = {head.Value}");
                    }
                    foreach (Cookie cookie in cookies) {
                       // Console.WriteLine($"Cookie: {cookie.Name} = {cookie.Value}");
                    }
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
