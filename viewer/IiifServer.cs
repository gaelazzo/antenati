using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace viewer {

    /// <summary>
    /// Il server IIIF delle immagini (API Image 2, {base}{codice}/{regione}/{dimensione}/{rotazione}/{qualita}.jpg):
    /// compone gli indirizzi di pagina intera, tasselli e info.json, legge info.json e, nell'altro verso,
    /// ricava il codice da un indirizzo d'immagine incollato.
    /// </summary>
    public class IiifServer {

        /// <summary>Il server in uso: si cambia solo dai test, per puntare a un server locale</summary>
        public static IiifServer current = new IiifServer("https://iiif-antenati.cultura.gov.it/iiif/2/");

        readonly string baseUrl;   // termina con "/"

        public IiifServer(string baseUrl) {
            this.baseUrl = baseUrl.EndsWith("/") ? baseUrl : baseUrl + "/";
        }

        /// <summary>Pagina intera larga width px; 0 = dimensione originale</summary>
        public string imageUrl(string code, int width) {
            return tileUrl(code, "full/" + (width > 0 ? width + "," : "max"));
        }

        /// <summary>Una regione, come "x,y,w,h/larghezza," (vedi tileRegions)</summary>
        public string tileUrl(string code, string regionAndSize) {
            return baseUrl + code + "/" + regionAndSize + "/0/default.jpg";
        }

        public string infoUrl(string code) {
            return baseUrl + code + "/info.json";
        }

        /// <summary>Filtro WebView2 che prende tutte le richieste verso questo server</summary>
        public string requestFilter => new Uri(baseUrl).GetLeftPart(UriPartial.Authority) + "/*";

        /// <summary>Codice dell'immagine in un indirizzo di questo server, null se l'indirizzo non e' suo</summary>
        public string codeFromImageUrl(string url) {
            if (url == null || !url.StartsWith(baseUrl)) return null;
            string code = url.Substring(baseUrl.Length).Split('/', '?', '#')[0].Trim();
            return code == "" ? null : code;
        }

        /// <summary>
        /// Regioni IIIF dei tasselli come le chiede il visualizzatore del sito: tasselli di tile px
        /// all'uscita, a fattore di scala scale (es. 3789x2784, 1024, 2 -> "0,0,2048,2048/1024," ...)
        /// </summary>
        public static string[] tileRegions(int width, int height, int tile, int scale) {
            var res = new List<string>();
            int span = tile * scale;
            for (int y = 0; y < height; y += span)
                for (int x = 0; x < width; x += span) {
                    int w = Math.Min(span, width - x), h = Math.Min(span, height - y);
                    int outW = (w + scale - 1) / scale;
                    res.Add($"{x},{y},{w},{h}/{outW},");
                }
            return res.ToArray();
        }

        public class Info {
            public int width, height;
            /// <summary>larghezza dei tasselli dichiarata dal server (1024 se non la dichiara)</summary>
            public int tileWidth;
        }

        public static Info parseInfo(string json) {
            var j = JObject.Parse(json);
            return new Info {
                width = j["width"]?.Value<int>() ?? 0,
                height = j["height"]?.Value<int>() ?? 0,
                tileWidth = j["tiles"]?[0]?["width"]?.Value<int>() ?? 1024,
            };
        }
    }
}
