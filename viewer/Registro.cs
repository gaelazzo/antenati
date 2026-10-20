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

        // Lettura nell'altro verso di href (+ "/codice pagina"): pagina incollata, redirect della ricerca, @id del manifest
        static readonly Regex hrefId = new Regex(@"an_ua(\d+)");
        static readonly Regex hrefPage = new Regex(@"an_ua\d+/([^/?#\s]+)");

        /// <summary>Codice del registro in un indirizzo ".../an_ua{id}...", null se non c'e'</summary>
        public static string idFromHref(string url) {
            var m = hrefId.Match(url ?? "");
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>Codice della pagina in un indirizzo ".../an_ua{id}/{codice}", null se non c'e'</summary>
        public static string pageCodeFromHref(string url) {
            var m = hrefPage.Match(url ?? "");
            return m.Success ? m.Groups[1].Value : null;
        }

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
            var all = JsonConvert.DeserializeObject<List<Registro>>(json);

            // I file salvati prima della correzione possono contenere piu' copie della stessa posizione
            // (ogni ri-esplorazione ne aggiungeva una, spesso vuota, in coda). Per ogni dato dell'utente
            // vale l'ultimo valore NON vuoto fra le copie del registro: "vince l'ultima copia" faceva
            // perdere l'indice collegato quando dopo c'era una copia vuota, e il Save successivo lo
            // cancellava per sempre. Per la descrizione vale la prima copia di ogni posizione, che e'
            // quella mostrata nell'albero; registroById punta all'ultima posizione, come prima.
            var merged = new Dictionary<string, UserData>();
            foreach (Registro r in all) {
                if (!merged.TryGetValue(r.idRegistro, out UserData d))
                    merged[r.idRegistro] = d = new UserData();
                d.take(r);
            }
            registri = new List<Registro>();
            var positions = new HashSet<string>();
            foreach (Registro r in all) {
                if (!positions.Add(r.idRegistro + "|" + r.parentKey))
                    continue;
                merged[r.idRegistro].applyTo(r);
                registri.Add(r);
                registroById[r.idRegistro] = r;
            }
        }

        /// <summary>Dati dell'utente di un registro, accumulati dalle sue copie in un file vecchio</summary>
        class UserData {
            string manifestId, idRegistroIndice, lastPageViewed;
            int nPaginaIndice;

            public void take(Registro r) {
                if (!string.IsNullOrEmpty(r.manifestId)) manifestId = r.manifestId;
                if (!string.IsNullOrEmpty(r.idRegistroIndice)) idRegistroIndice = r.idRegistroIndice;
                if (r.nPaginaIndice != 0) nPaginaIndice = r.nPaginaIndice;
                if (!string.IsNullOrEmpty(r.lastPageViewed)) lastPageViewed = r.lastPageViewed;
            }

            public void applyTo(Registro r) {
                r.manifestId = manifestId;
                r.idRegistroIndice = idRegistroIndice;
                r.nPaginaIndice = nPaginaIndice;
                r.lastPageViewed = lastPageViewed;
            }
        }

        /// <summary>
        /// Copia i dati dell'utente (manifest, indice, ultima pagina vista), che sono per registro e non per
        /// posizione nell'albero: un indice decennale sta sia sotto il primo sia sotto l'ultimo anno.
        /// </summary>
        void copyUserData(Registro from) {
            if (from == this)
                return;
            manifestId = from.manifestId;
            manifest = from.manifest;
            idRegistroIndice = from.idRegistroIndice;
            nPaginaIndice = from.nPaginaIndice;
            lastPageViewed = from.lastPageViewed;
        }

        /// <summary>
        /// Registro in una posizione dell'albero (serie/anno/tipologia). Se la posizione e' gia' nota restituisce
        /// l'istanza esistente, cosi' una ri-esplorazione non crea copie e non stacca i dati dell'utente;
        /// se il registro e' noto in un'altra posizione la nuova istanza ne eredita i dati.
        /// </summary>
        static Registro findOrCreate(string anno, string idSerie, string kind, string idRegistro, string segnatura) {
            string parent = AnnoSerieKind.objectKey(idSerie, anno, kind);
            var existing = registri.FirstOrDefault(x => x.idRegistro == idRegistro && x.parentKey == parent);
            if (existing != null)
                return existing;
            Registro r = new Registro(anno, idSerie, kind, idRegistro, segnatura);
            if (registroById.TryGetValue(idRegistro, out Registro known))
                r.copyUserData(known);
            else
                registroById[idRegistro] = r;
            registri.Add(r);
            return r;
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
            // Il codice modifica i dati dell'utente sull'istanza di registroById: le altre posizioni
            // dello stesso registro vanno allineate, altrimenti al caricamento potrebbe vincere una copia vecchia
            foreach (Registro r in registri) {
                if (registroById.TryGetValue(r.idRegistro, out Registro current))
                    r.copyUserData(current);
            }

            // Converte la lista in una stringa JSON
            string json = JsonConvert.SerializeObject(registri, Formatting.Indented);

            // Salva il JSON nel file specificato
            SafeFile.WriteAllText(filePath, json);
        }
        public static List<IArchiveNode> deriveRegistroFromPage(string anno, string idSerie,string kind, string url) {
            List<IArchiveNode> res = new List<IArchiveNode>();
            string idRegistro = idFromHref(url);
            if (idRegistro != null) {
                string segnaturaAttuale = "unico";
                res.Add(findOrCreate(anno, idSerie, kind, idRegistro, segnaturaAttuale));
                return res;
            }
            else {
                //MessageBox.Show($"Codice non trovato nella pagina {url}", "Errore");
                return null;
            }
        }

            public static List<IArchiveNode> read(string anno, string idSerie, string kind) {
            AnnoSerieKind annoSerieKind = AnnoSerieKind.annoSerieKindById[AnnoSerieKind.objectKey(idSerie,anno,kind)];

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
            return parseRegistri(anno, idSerie, kind, htmlContent);
        }

        /// <summary>
        /// Estrae i registri dalla pagina di ricerca serie/anno/tipologia e li aggiunge a registri/registroById
        /// </summary>
        public static List<IArchiveNode> parseRegistri(string anno, string idSerie, string kind, string htmlContent) {
            List<IArchiveNode> res = new List<IArchiveNode>();
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
                               
                res.Add(findOrCreate(anno, idSerie, kind, idRegistro, segnaturaAttuale));
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
