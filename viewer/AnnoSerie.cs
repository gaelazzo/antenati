using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.IO;
using System.Security.Cryptography;
using System.Net.Http;
using System.Security.Policy;
using static System.Net.Mime.MediaTypeNames;
using System.Web.UI.WebControls;

namespace viewer {
    public class AnnoSerie : IArchiveNode {
        
        public const string AnnoSeriesName = "AnnoSeries.json";
        [JsonIgnore]
        public tipoNodo tipo { get { return tipoNodo.AnnoSerie; } }

        public static List<AnnoSerie> annoSeries = new List<AnnoSerie>();
        public static Dictionary<string, AnnoSerie> annoSerieById = new Dictionary<string, AnnoSerie>();

        const string AnnoSeriesIndexUrl = "https://antenati.cultura.gov.it/search-registry/?serie={idSerie}";
        const string AnnoSeriesIndexUrl2 = "https://antenati.cultura.gov.it/search-registry/?localita={localita}&s_facet_query=anni_is%3A{anno}";

        [JsonIgnore]
        public string href { get { return $"https://antenati.cultura.gov.it/search-registry/?serie={idSerie}&s_facet_query=anni_is%3A{pureAnno(anno)}"; } }

        public static string getIndexUrl2(string idSerie, string anno) {
            return AnnoSeriesIndexUrl2.Replace("{localita}", Uri.EscapeDataString(Serie.serieById[idSerie].localita ?? Serie.serieById[idSerie].description)).Replace("{anno}", pureAnno(anno));
        }

        [JsonIgnore]
        public string description { get; set; }

        [JsonIgnore]
        public string idArchivio { get; set; }
        [JsonIgnore]
        public string idFondo { get; set; }

        public string idSerie { get; set; }

        public List<IArchiveNode> explore() {
            return AnnoSerieKind.read(key);
        }

        static string pureAnno(string anno) { return anno.Split('-')[0]; }

        public string anno { get; set; }
        [JsonIgnore]
        public string idSerieAnno { get { return objectKey(idSerie, anno);  }  }
        [JsonIgnore]
        public string key => idSerieAnno;
        [JsonIgnore]
        public string parentKey => idSerie;
        [JsonIgnore]
        public IArchiveNode parentElement => Serie.serieById[idSerie];

        public static string objectKey(string idSerie, string anno) {
            return idSerie + "/" + anno;
        }
        public static void Load(string filePath = AnnoSeriesName) {
            // Leggi il contenuto del file JSON
            string json = File.ReadAllText(filePath);

            // Converte la stringa JSON in una lista di oggetti Registro
            annoSeries = JsonConvert.DeserializeObject<List<AnnoSerie>>(json);
            List<AnnoSerie> clean = new List<AnnoSerie>();
            foreach (AnnoSerie s in annoSeries) {
                if (!annoSerieById.ContainsKey(s.key)) {
                    annoSerieById[s.key] = s;
                    clean.Add(s);
                }
            }
            annoSeries= clean;

        }
        static string getIndexUrl(string idSerie) {
            return AnnoSeriesIndexUrl.Replace("{idSerie}", idSerie);
        }

        public static void Save(string filePath = AnnoSeriesName) {
            // Converte la lista in una stringa JSON
            string json = JsonConvert.SerializeObject(annoSeries, Formatting.Indented);

            // Salva il JSON nel file specificato
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// Obtains information about AnnoSerie from page Registro
        /// </summary>
        /// <param name="html"></param>
        /// <returns></returns>
        public static List<IArchiveNode> deriveAnnoSerieFromRegistroPage(string idSerie, string html,string url) {
            List<IArchiveNode> res = new List<IArchiveNode>();
            /*
             * address: https://antenati.cultura.gov.it/ark:/12657/an_ua274176/LDbXr7G
              <article><header><div>
                        <h2>Registro: 1809</h2>
                        </div>
                    <aside></aside>
                    </header>              
                <hr>
               <div class="detail-registry">
                <div><p><a href="/search-registry/?tipologia=Morti&serie=287143"><strong>Morti</strong></a>
                          </strong></p>  
                    <p><a href="/search-registry/?fondo=281500&descrizione=Stato civile napoleonico">Stato civile napoleonico</a>&nbsp; > &nbsp;
                         <a href="/search-registry/?serie=287143&descrizione=Terlizzi">Terlizzi</a>
                    </p>
                    <p>
                <strong>Conservato da:</strong>
                <a href="/archivio/archivio-di-stato-di-bari" target="_self" title="">Archivio di Stato di Bari</a></p>        
                <p><strong>Comune/Località:</strong><a href="/search-registry/?localita=Terlizzi">Terlizzi</a></p>
             </div>      
            */
            var yearPattern = @"<h2>\s*Registro:\s*(\d{4}(?:-\d{4})?)\s*</h2>";
            var yearMatch = Regex.Match(html, yearPattern, RegexOptions.Singleline);
            if (yearMatch.Success) {
                
                string anno = yearMatch.Groups[1].Value;

                AnnoSerie AS = new AnnoSerie( idSerie, anno);
                if (annoSerieById.ContainsKey(AS.key)) {
                    AS = annoSerieById[AS.key];
                }
                else {
                    annoSeries.Add(AS);
                    annoSerieById[AS.key] = AS;
                }
                res.Add(AS);

                var child = AnnoSerieKind.deriveKindFromRegistroPage(anno, idSerie, html,url);   
                if (child != null) {
                    foreach(var c in child) res.Add(c);
                }
                
            }

            // Pattern per estrarre la tipologia dal link <a>
            //var typePattern = @"<a href=""/search-registry/\?tipologia=([^&""]+)&";
            //var typeMatch = Regex.Match(html, typePattern);
            //if (typeMatch.Success) {
            //    Console.WriteLine("Tipologia: " + typeMatch.Groups[1].Value);
            //}
            

            return res;
        }
        public static List<IArchiveNode> read(string idSerie) {
            Serie serie = Serie.serieById[idSerie];
            List<IArchiveNode> res = new List<IArchiveNode>();
            string url = getIndexUrl(serie.idSerie);
            var response = PageLoader.getResponseMessage(url,HttpCompletionOption.ResponseHeadersRead);
            if (response.RequestMessage.RequestUri.AbsoluteUri != url) {
                Console.WriteLine($"302 Found: Redirected from {url} to {response.RequestMessage.RequestUri.AbsoluteUri}");

                // Segui il reindirizzamento se necessario
                return deriveAnnoSerieFromRegistroPage(idSerie, 
                            response.Content.ReadAsStringAsync().Result,
                            response.RequestMessage.RequestUri.AbsoluteUri);

            }
            // Contenuto della pagina finale
            var htmlContent =  response.Content.ReadAsStringAsync().Result;                
            

            // Trova l'elenco degli anni della serie
            /*  <ul class="flex-list">
             * <!--{"q":"+model_s:\"fondo\" +livello_superiore_i:idFondo","rows":"9999","wt":"json"}-->
             *  <li><a href="/search-registry/?serie=310644&descrizione=Accettura" title="">Accettura</a></li>
             *  <li><a href="/search-registry/?serie=311489&descrizione=Alianello (oggi Aliano)" title="">
             *  ..
             *  </ul>
             */

            // Pattern per catturare la `div` con classe `facet-modal-anni_is`
            
            string pattern = @"<a[^>]*data-facet-term=""anni_is:(\d{4})""[^>]*>";

            var yearMatches = Regex.Matches(htmlContent, pattern, RegexOptions.Singleline );

            HashSet<string> matches = new HashSet<string>();

            foreach (Match liMatch in yearMatches) {
                string anno = liMatch.Groups[1].Value;
                if (matches.Contains(anno))continue;
                matches.Add(anno);

                AnnoSerie AS = new AnnoSerie(idSerie, anno);
                annoSeries.Add(AS);
                res.Add(AS);
                annoSerieById[AS.key] = AS;
            }
           
            return res;

        }

        static AnnoSerie() {
            if (File.Exists(AnnoSeriesName)) {
                Load();
            }
            
        }


        public AnnoSerie(string idSerie, string anno) {
            this.anno = anno;
            this.idSerie = idSerie;
            this.description  = anno;
            Serie s = Serie.serieById[idSerie];
            this.idFondo = s.idFondo;
            this.idArchivio = s.idArchivio;    
            s.myAnnoSeries.Add(this);
        }
    }
}
