using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections;
using System.ComponentModel;
using System.Data;
using System.Web;
using System.Net;
using System.Security.Policy;
using System.Net.Http;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using System.IO;
using System.Windows.Forms;

namespace viewer {


    public class Registro : IArchiveNode  {        
        public const string RegistroName = "Registro.json";
        [JsonIgnore]
        public tipoNodo tipo { get { return tipoNodo.Registro; } }

        public static List<Registro> registri = new List<Registro>();
        public static Dictionary<string, Registro> registroById = new Dictionary<string, Registro>();
        static string pureAnno(string anno) { return anno.Split('-')[0]; }

        const string RegistroAddress = "https://antenati.cultura.gov.it/search-registry/?serie={idSerie}&s_facet_query=tipologia_ss%3A{kind}%252Canni_is%3A{anno}";


        const string RegistroAddress2 = "https://antenati.cultura.gov.it/search-registry/?localita={localita}&s_facet_query=tipologia_ss%3A{kind}%252anni_is%3A{anno}";
        [JsonIgnore]
        public string href { get { return $"https://antenati.cultura.gov.it/ark:/12657/an_ua{idRegistro}"; } }

        [JsonIgnore]
        public string idSerieAnnoKind { get { return idSerie + "/" + anno + "/" + kind; } }

        public static string objectKey(string idSerie, string anno, string kind) {            
            return idSerie + "/" + anno + "/" + kind;
        }

        public string description { get; set; }
        [JsonIgnore]
        public string idArchivio { get; set; }

        [JsonIgnore]
        public string idFondo { get; set; }
                
        public string idSerie { get; set; }
        
        public string kind { get; set; }

        public string anno { get; set; }

        public string manifestId { get; set; }

        public string idRegistro { get; set; }
        public string idRegistroIndice { get; set; }
        public int nPaginaIndice { get; set; }

        public string lastPageViewed { get; set; }


        [JsonIgnore]
        public Manifest manifest { get; set; } 

        [JsonIgnore]
        public string key => idRegistro;
        [JsonIgnore]
        public string parentKey => AnnoSerieKind.objectKey(idSerie,anno,kind);
        [JsonIgnore]
        public IArchiveNode parentElement => AnnoSerieKind.annoSerieKindById[parentKey];

        

        public List<IArchiveNode> explore() {
            if (manifestId == null) {
                manifestId = ManifestIdExtractor.ExtractManifestId(href);
            }
            if (manifest==null&& manifestId != null){                
                manifest = Manifest.LoadManifest(manifestId).Result;
            }            
            return new List<IArchiveNode>();
        }


        public static void Load(string filePath = RegistroName) {
            // Leggi il contenuto del file JSON
            string json = File.ReadAllText(filePath);

            // Converte la stringa JSON in una lista di oggetti Registro
            registri = JsonConvert.DeserializeObject<List<Registro>>(json);
            foreach (Registro s in registri) {
                registroById[s.idRegistro] = s;
            }
        }
        static string fixKind(string kind) {
            if (kind.Contains(",")) kind = "\"" + kind + "\"";
            return Uri.EscapeDataString(kind);
        }
        static string getIndexUrl(string idSerie, string anno, string kind) {
            return RegistroAddress.Replace("{idSerie}", idSerie).Replace("{anno}", pureAnno(anno)).Replace("{kind}", fixKind(kind));
        }
        public static string getIndexUrl2(string idSerie, string anno, string kind) {
            return RegistroAddress2.Replace("{localita}", Uri.EscapeDataString(Serie.serieById[idSerie].localita?? Serie.serieById[idSerie].description)).Replace("{anno}", pureAnno(anno)).Replace("{kind}", fixKind(kind));
        }

        public static void Save(string filePath = RegistroName) {
            // Converte la lista in una stringa JSON
            string json = JsonConvert.SerializeObject(registri, Formatting.Indented);

            // Salva il JSON nel file specificato
            File.WriteAllText(filePath, json);
        }
        public static List<IArchiveNode> deriveRegistroFromPage(string anno, string idSerie,string kind, string url) {
            List<IArchiveNode> res = new List<IArchiveNode>();
            string mask = @"an_ua(\d+)";
            Match match = Regex.Match(url, mask);
            if (match.Success) {
                string idRegistro = match.Groups[1].Value; // Estrarre il codice
                string segnaturaAttuale = "unico";
                Registro r = new Registro(anno, idSerie, kind, idRegistro, segnaturaAttuale);
                registri.Add(r);
                res.Add(r);
                registroById[r.idRegistro] = r;
                return res;
            }
            else {
                //MessageBox.Show($"Codice non trovato nella pagina {url}", "Errore");
                return null;
            }
        }

            public static List<IArchiveNode> read(string anno, string idSerie, string kind) {
            AnnoSerieKind annoSerieKind = AnnoSerieKind.annoSerieKindById[AnnoSerieKind.objectKey(idSerie,anno,kind)];
            List<IArchiveNode> res = new List<IArchiveNode>();
            
            string url = getIndexUrl(annoSerieKind.idSerie, annoSerieKind.anno, annoSerieKind.kind);            
            // Trova l'elenco dei tipi della serie/anno
            /*  <a href="https://antenati.cultura.gov.it/search-registry/?serie=262843&amp;descrizione=Modugno&amp;s_facet_query=anni_is%3A1826&amp;lang=it" title="" data-facet-term="tipologia_ss:&quot;Nati, indice&quot;">
                    <span>Nati, indice</span> <small>(2)</small>
                  </a>
             */

            
            var response = PageLoader.getResponseMessage(url, HttpCompletionOption.ResponseHeadersRead);
            if (response.RequestMessage.RequestUri.AbsoluteUri != url) {
                Console.WriteLine($"302 Found: Redirected to {response.RequestMessage.RequestUri.AbsoluteUri}");
                
               

                // Segui il reindirizzamento se necessario
                return deriveRegistroFromPage(anno, idSerie, kind, response.RequestMessage.RequestUri.AbsoluteUri);
                // Segui il reindirizzamento se necessario


            }

            // Contenuto della pagina finale
            string htmlContent = response.Content.ReadAsStringAsync().Result;

            if (htmlContent.Contains("<strong>Nessun risultato trovato</strong>")) {
                url = getIndexUrl2(annoSerieKind.idSerie, annoSerieKind.anno, annoSerieKind.kind);
                response = PageLoader.getResponseMessage(url, HttpCompletionOption.ResponseHeadersRead);
                if (response.RequestMessage.RequestUri.AbsoluteUri != url) {
                    Console.WriteLine($"302 Found: Redirected to {response.RequestMessage.RequestUri.AbsoluteUri}");

                    // Segui il reindirizzamento se necessario
                    return deriveRegistroFromPage(anno, idSerie, kind, response.RequestMessage.RequestUri.AbsoluteUri);
                    // Segui il reindirizzamento se necessario

                }
                htmlContent = response.Content.ReadAsStringAsync().Result;
            }
            /*
             * 
             * <li class="search-item" data-id="274175">
              <div>
                <h3 class="text-primary">
                  <a href="/ark:/12657/an_ua274175" class="" title="">
                    Registro: 1822                                      </a>
                </h3>
                <p>
                                      <strong>Nati</strong>
                                      </strong>
                </p>
                                  <p>
                    <strong>Segnatura attuale:</strong> Parte 1                  </p>
            */
            // Pattern per catturare la `div` con classe `facet-modal-anni_is`
            //string pattern = @"<li class=""search-item"" data-id=""(\d+)"".*?>(?:[\s\S]*?Segnatura attuale:</strong>\s*(.*?)\s*(?:</p>)?[\s\S]*?</div>\s*</li>)";
            //string pattern = @"<li class=""search-item"" data-id=""(\d+)"".*?>(?:.*?Segnatura attuale:</strong>\s*(.*?)\s*</p>)?";
            //string pattern = @"<li class=""search-item"" data-id=""(\d+)"".*?href=""/ark:/12657/an_ua(\d+)"".*?>.*?(?:Segnatura attuale:</strong>\s*(.*?)\s*</p>)?";
            //string pattern = @"<li class=""search-item"" data-id=""(\d+)"".*?href=""/ark:/12657/an_ua\d+"".*?>.*?Registro:\s*([\d/\-]+).*?(?:Segnatura attuale:</strong>\s*(.*?)\s*</p>)?";
            //string pattern = @"<li class=""search-item"" data-id=""(\d+)"".*?href=""/ark:/12657/an_ua\d+"".*?(?:Registro:\s*([\d/\-]+))?.*?(?:Segnatura attuale:</strong>\s*(.*?)\s*</p>)?";
            //string pattern = @"<li class=""search-item"" data-id=""(\d+)"".*?href=""/ark:/12657/an_ua\d+"".*?(?:Registro:\s*([\d/\-]+))?.*?(?:Segnatura attuale:\s*</strong>\s*(.*?)\s*</p>)?";
            //string pattern = @"<li class=""search-item"" data-id=""(\d+)"".*?href=""/ark:/12657/an_ua\d+"".*?(?:Registro:\s*([\d/\-]+))?.*?(?:Segnatura attuale:\s*</strong>\s*([^<]*)\s*</p>)?";
            //string pattern = @"<li class=""search-item"" data-id=""(\d+)"".*?href=""/ark:/12657/an_ua\d+"".*?Registro:\s*([\d/\-]+)?\s*</a>.*?(?:Segnatura attuale:\s*</strong>\s*([^<]*)\s*</p>)?";
            //string pattern = @"<li class=""search-item"" data-id=""(\d+)"".*?href=""/ark:/12657/an_ua\d+"".*?Registro:\s*([\d/\-]+)?\s*</a>.*?(?:<strong>Segnatura attuale:</strong>\s*([^<]+)\s*</p>)?";

            //string pattern = @"<li class=""search-item"" data-id=""(\d+)"".*?href=""/ark:/12657/an_ua\d+"".*?Registro:\s*([\d/\-]+)?.*?<strong>Segnatura attuale:</strong>\s*([^<]+)";
            //string pattern = @"<li class=""search-item"" data-id=""(\d+)"".*?href=""/ark:/12657/an_ua\d+"".*?Registro:\s*([\d/\-]+)?.*?(?:<strong>Segnatura attuale:</strong>\s*([^<]+))?";
            //string pattern = @"<li class=""search-item"" data-id=""(\d+)"".*?href=""/ark:/12657/an_ua\d+"".*?Registro:\s*([\d/\-]+)?.*?<strong>Segnatura attuale:\s*</strong>\s*(.*?)\s*</p>";
            //string pattern = @"<li class=""search-item"" data-id=""(\d+)"".*?href=""/ark:/12657/an_ua\d+"".*?Registro:\s*([\d/\-]+)?.*?(?:<strong>Segnatura attuale:\s*</strong>\s*(.*?)\s*</p>)?.*</div>";
            //string pattern = @"<li class=""search-item"" data-id=""(\d+)"".*?href=""/ark:/12657/an_ua\d+"".*?Registro:\s*([\d/\-]+)?.*?(?:<strong>\s*Segnatura attuale:\s*</strong>\s*(.*?)\s*</p>)?";
            //string pattern = @"<li class=""search-item"" data-id=""(\d+)"".*?href=""/ark:/12657/an_ua\d+"".*?Registro:\s*([\d/\-]+)?.*?(?:<strong>\s*Segnatura attuale:\s*</strong>\s*(.*?)\s*</p>)?";
            //string pattern = @"<li class=""search-item"" data-id=""(\d+)"".*?href=""/ark:/12657/an_ua\d+"".*?Registro:\s*([\d/\-]+).*?(?:<strong>\s*Segnatura attuale:\s*</strong>\s*([^<]*))?";
            //string pattern = @"<li class=""search-item"" data-id=""(\d+)"".*?href=""/ark:/12657/an_ua\d+"".*?Registro:\s*([^<]+).*?(?:<strong>Segnatura attuale:</strong>\s*([^<]*))?.*?</div>";
            //string pattern = @"<li class=""search-item"" data-id=""(\d+)"".*?href=""/ark:/12657/an_ua\d+""[^>]*>\s*Registro:\s*(\d+)\s*</a>.*?(?:<strong>Segnatura attuale:</strong>\s*([^<]*))?.*?</div>";
            //string pattern = @"<li class=""search-item"" data-id=""(\d+)"".*?href=""/ark:/12657/an_ua\d+""[^>]*>\s*Registro:\s*(\d+)\s*</a>.*?(?:<strong>Segnatura attuale:</strong>\s*([^<]*))?\s*</div>";
            string pattern = @"<li class=""search-item"" data-id=""(\d+)"".*?href=""/ark:/12657/an_ua\d+""[^>]*>\s*Registro:\s*(.*?)\s*</a>.*?(?:<strong>Segnatura attuale:</strong>\s*([^<]*))?.*?</div>";


            //string pattern = @"<li class=""search-item"" data-id=""(\d+)"".*?href=""/ark:/12657/an_ua\d+"".*?Registro:\s*([\d/\-]+)</div>";





            var currKind = annoSerieKind.kind;
            //string pattern = @"<li class=""search-item"" data-id=""(\d+)"".*?>.*?Segnatura attuale:</strong>\s*(.*?)\s*</p>";
            var matches = Regex.Matches(htmlContent, pattern, RegexOptions.Singleline);

            foreach (Match match in matches ) {
                string idRegistro = match.Groups[1].Value;
                string registro = match.Groups[2].Success ? match.Groups[2].Value.Trim() : "Nessun registro";
                string m = match.Value;
                string segnaturaAttuale = (matches.Count == 1 ? "unico" : registro);
                if (m.IndexOf("Segnatura attuale") > 0) {
                    int seg = m.IndexOf("Segnatura attuale");
                    int res1 = m.IndexOf("</strong>", seg);
                    int stop = m.IndexOf("</p>",res1);
                    segnaturaAttuale = m.Substring(res1 + 9, stop - res1 - 9).Trim();
                }
                //string segnaturaAttuale = match.Groups[3].Success ? match.Groups[3].Value.Trim() : (matches.Count ==1? "unico": registro);
                               
                Registro r = new Registro(anno,idSerie,kind, idRegistro, segnaturaAttuale);
                registri.Add(r);
                res.Add(r);
                registroById[r.idRegistro] = r;
            }
            return res;
        }

        static Registro() {
            if (File.Exists(RegistroName)) {
                Load();
            }
            
        }


        public Registro(string anno, string idSerie, string kind, string idRegistro, string segnatura = "unico") {
            this.anno = anno;
            this.idSerie = idSerie;
            this.kind = kind;
            this.idRegistro = idRegistro;
            AnnoSerieKind a = AnnoSerieKind.annoSerieKindById[idSerieAnnoKind];
            this.idFondo = a.idFondo;
            this.idArchivio = a.idArchivio;            
            this.description = segnatura;
            
        }
    }
}
