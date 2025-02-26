using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.IO;
using Microsoft.SqlServer.Server;
using System.Net.Http;
using System.Security.Policy;

namespace viewer {
    public class AnnoSerieKind : IArchiveNode {

        public const string AnnoSeriesKindName = "AnnoSeriesKind.json";
        [JsonIgnore]
        public tipoNodo tipo { get { return tipoNodo.AnnoSerieKind; } }

        public static List<AnnoSerieKind> annoSeriesKind = new List<AnnoSerieKind>();
        public static Dictionary<string, AnnoSerieKind> annoSerieKindById = new Dictionary<string, AnnoSerieKind>();

        const string AnnoSeriesKindIndexUrl = "https://antenati.cultura.gov.it/search-registry/?serie={idSerie}&s_facet_query=anni_is%3A{anno}";
        

        static string fixKind(string kind) {
            if (kind.Contains(","))
                kind = "\"" + kind + "\"";
            return Uri.EscapeDataString(kind);
        }

        [JsonIgnore]
        public string href { get { return $"https://antenati.cultura.gov.it/search-registry/?serie={idSerie}&s_facet_query=tipologia_ss%3A{fixKind(kind)}%252Canni_is%3A{pureAnno(anno)}"; ; } }
      
        public string description { get; set; }

        [JsonIgnore]
        public string idArchivio { get; set; }
        [JsonIgnore]
        public string idFondo { get; set; }
        
        public string idSerie { get; set; }

        public string kind { get; set; }
        
        public string anno { get; set; }

        static string pureAnno(string anno) { return anno.Split('-')[0]; }

        [JsonIgnore]
        public string idSerieAnnoKind { get { return objectKey(idSerie, anno, kind); } }

        [JsonIgnore]
        public string key => idSerieAnnoKind;

        [JsonIgnore]
        public string parentKey => AnnoSerie.objectKey(idSerie,anno);

        [JsonIgnore]
        public IArchiveNode parentElement => AnnoSerie.annoSerieById[parentKey];

        public static string objectKey(string idSerie, string anno, string kind) {
            return idSerie + "/" + anno + "/" + kind;
        }

        public List<IArchiveNode> explore() {
            return Registro.read(anno,idSerie,kind);
        }


        public static void Load(string filePath = AnnoSeriesKindName) {
            // Leggi il contenuto del file JSON
            string json = File.ReadAllText(filePath);

            // Converte la stringa JSON in una lista di oggetti Registro
            annoSeriesKind = JsonConvert.DeserializeObject<List<AnnoSerieKind>>(json);
            foreach (AnnoSerieKind s in annoSeriesKind) {
                annoSerieKindById[s.idSerieAnnoKind] = s;
            }
        }
        static string getIndexUrl(string idSerie, string anno) {
            return AnnoSeriesKindIndexUrl.Replace("{idSerie}", idSerie).Replace("{anno}", pureAnno(anno));
        }

        public static void Save(string filePath = AnnoSeriesKindName) {
            // Converte la lista in una stringa JSON
            string json = JsonConvert.SerializeObject(annoSeriesKind, Formatting.Indented);

            // Salva il JSON nel file specificato
            File.WriteAllText(filePath, json);
        }

        public static List<IArchiveNode> deriveKindFromRegistroPage(string anno, string idSerie, string html, string url) {
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
            var typePattern = @"<a href=""/search-registry/\?tipologia=([^&""]+)&";
            foreach (Match match in Regex.Matches(html, typePattern, RegexOptions.Singleline)) {
                var tipo = match.Groups[1].Value;
                AnnoSerieKind a = new AnnoSerieKind(anno,idSerie, tipo);
                annoSeriesKind.Add(a);
                res.Add(a);
                annoSerieKindById[a.idSerieAnnoKind] = a;

                var child = Registro.deriveRegistroFromPage(anno, idSerie, tipo, html);
                if (child != null) {
                    foreach (var c in child)
                        res.Add(c);
                }
            }
            
            return res;
        }

        public static List<IArchiveNode> read(string idAnnoSerie) {
            List<IArchiveNode> res = new List<IArchiveNode>();
            AnnoSerie annoSerie= AnnoSerie.annoSerieById[idAnnoSerie];

            
            string url = getIndexUrl(annoSerie.idSerie, annoSerie.anno);
            var response = PageLoader.getResponseMessage(url, HttpCompletionOption.ResponseHeadersRead);
            if (response.RequestMessage.RequestUri.AbsoluteUri != url) {
                Console.WriteLine($"302 Found: Redirected to {response.RequestMessage.RequestUri.AbsoluteUri}");

                // Segui il reindirizzamento se necessario
                return deriveKindFromRegistroPage(annoSerie.anno, annoSerie.idSerie, 
                        response.Content.ReadAsStringAsync().Result,
                        response.RequestMessage.RequestUri.AbsoluteUri);

            }

            // Contenuto della pagina finale
            string htmlContent = response.Content.ReadAsStringAsync().Result;

            // Trova l'elenco dei tipi della serie/anno
            /*  <a href="https://antenati.cultura.gov.it/search-registry/?serie=262843&amp;descrizione=Modugno&amp;s_facet_query=anni_is%3A1826&amp;lang=it" title="" data-facet-term="tipologia_ss:&quot;Nati, indice&quot;">
                    <span>Nati, indice</span> <small>(2)</small>
                  </a>
             */

            // Pattern per catturare la `div` con classe `facet-modal-anni_is`
            string pattern = @"data-facet-term=""tipologia_ss:(?:&quot;)?([^""]+?)(?:&quot;)?""";


            foreach (Match match in Regex.Matches(htmlContent, pattern,RegexOptions.Singleline)) {
                var tipo = match.Groups[1].Value;
                if (annoSerieKindById.ContainsKey(AnnoSerieKind.objectKey( annoSerie.idSerie, annoSerie.anno, tipo)))
                    continue; //errore nella pagina, saltiamo il duplicato
                AnnoSerieKind a = new AnnoSerieKind(annoSerie.anno, annoSerie.idSerie, tipo);
                annoSeriesKind.Add(a);
                res.Add(a);
                annoSerieKindById[a.idSerieAnnoKind]=a;
            }
            return res;
        }

        static AnnoSerieKind() {
            if (File.Exists(AnnoSeriesKindName)) {
                Load();
            }
           
        }


        public AnnoSerieKind(string anno, string idSerie, string kind) {
            this.anno = anno;
            this.idSerie = idSerie;
            this.kind = kind;

            AnnoSerie s = AnnoSerie.annoSerieById[AnnoSerie.objectKey(idSerie,anno)];
            this.idFondo = s.idFondo;
            this.idArchivio = s.idArchivio;            
            this.description = kind;
        }
    }
}
