using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using System.IO;
using System.Runtime.Remoting.Messaging;

namespace viewer {
    public class Fondo : IArchiveNode {
        public const string FondiName = "Fondi.json";
        [JsonIgnore]
        public tipoNodo tipo { get { return tipoNodo.Fondo; } }

        public static List<Fondo> fondi = new List<Fondo>();
        public static Dictionary<string, Fondo> fondoById = new Dictionary<string, Fondo>();

        const string FondoIndexUrl = "https://antenati.cultura.gov.it/archive/?archivio={idArchivio}";

        public string description { get; set; }
        public string idArchivio { get; set; }
        public string idFondo { get; set; }

        [JsonIgnore]
        public string key => idFondo;
        [JsonIgnore]
        public IArchiveNode parentElement => Archivio.archivioById[parentKey];
        [JsonIgnore]
        public string parentKey => idArchivio;

        public List<IArchiveNode> explore() {
            return Serie.read(key);
        }

        [JsonIgnore]
        public string href { get { return $"https://antenati.cultura.gov.it/archive/?archivio={idArchivio}"; } }

        public static void Load(string filePath = FondiName) {
            // Leggi il contenuto del file JSON
            string json = File.ReadAllText(filePath);

            // Converte la stringa JSON in una lista di oggetti Registro
            fondi = JsonConvert.DeserializeObject<List<Fondo>>(json);
            List<Fondo> clean = new List<Fondo>();
            foreach (Fondo a in fondi) {
                if (!fondoById.ContainsKey(a.idFondo)) {
                    fondoById[a.idFondo] = a;
                    clean.Add(a);
                }
            }
            fondi = clean;
        }
        static string getIndexUrl(string idArchivio) {
            return FondoIndexUrl.Replace("{idArchivio}", idArchivio);
        }

        public static void Save(string filePath = FondiName) {
            // Converte la lista in una stringa JSON
            string json = JsonConvert.SerializeObject(fondi, Formatting.Indented);

            // Salva il JSON nel file specificato
            File.WriteAllText(filePath, json);
        }
        
        public static List<IArchiveNode> read(string idArchivio) {
            List<IArchiveNode> res = new List<IArchiveNode>();
            //var w = new WebClient();
            //w.Encoding = Encoding.UTF8;
            string url = getIndexUrl(idArchivio);
            //var htmlContent = w.DownloadString(url);
            var htmlContent = PageLoader.getPage(url);

            // Trova il nome dei fondi
            /* <div id="archive_property_select" hidden>
                    <span class="label">Fondo</span><br>
                    <select>
                        <option value="310643">Stato civile della restaurazione</option>
                        <option value="335552">Stato civile italiano</option>  
                        <option value="343790">Stato civile napoleonico</option>
                    </select>
                </div>
             */
            //string pattern = @"<div\s+id=""archive_property_select"".*?>\s*<span[^>]*>[^<]*<\/span><br>\s*<select>(.*?)<\/select>\s*<\/div>";
            string pattern = @"<div\s+id=""archive_property_select""[^>]*?>([\s\S]*?)<\/div>";



            var mainMatch = Regex.Match(htmlContent, pattern, RegexOptions.Singleline);

            if (mainMatch.Success) {
                string selectContent = mainMatch.Groups[1].Value;
                /**
                 *   <span class="label">Fondo</span><br>
                    <select> <option value="242623">Stato civile della restaurazione</option>
                             <option value="281500">Stato civile napoleonico</option>
                            <option value="3804025">Stato civile italiano</option>
                            <option value="20352649">Inventario</option>
                    </select>
                */

                // Estrarre i codici e i nomi.
                //var matches = Regex.Matches(selectContent, @"<option\s+value=""(\d+)"">\s*(.*?)\s*<\/option>");
                var matches = Regex.Matches(selectContent, @"<option\s+value=['""](\d+)['""]\s*>\s*(.*?)\s*</option>", RegexOptions.Singleline);


                foreach (Match match in matches) {
                    string codice = match.Groups[1].Value;
                    string nome = match.Groups[2].Value;
                    var f = new Fondo(idArchivio, codice, nome);
                    fondi.Add(f);
                    res.Add(f);
                    fondoById[codice] = f;
                }
            }
            return res;
        }

        static Fondo() {
            if (File.Exists(FondiName)) {
                Load();
            }
           
        }


        public Fondo(string idArchivio, string idFondo, string description) {
            this.idFondo = idFondo;
            this.idArchivio = idArchivio;
            this.description = description;
        }
    }
}
