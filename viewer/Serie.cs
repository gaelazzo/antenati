using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.IO;
using System.Windows.Forms;

namespace viewer {
    

    public class Serie : IArchiveNode {
        /*
        * 
        string pattern = @"<ul class=""flex-list"">\s*<!--\{""q"":""\+model_s:\\\""fondo\\\"" \+livello_superiore_i:310643.*?-->\s*([\s\S]*?)<\/ul>";
Regex regex = new Regex(pattern);

       */

        public const string SeriesName = "Series.json";
        [JsonIgnore]
        public tipoNodo tipo { get { return tipoNodo.Serie; } }

        public static List<Serie> series = new List<Serie>();
        public static Dictionary<string, Serie> serieById = new Dictionary<string, Serie>();

        const string SeriesIndexUrl = "https://antenati.cultura.gov.it/archive/?archivio={idArchivio}";

        const string SeriesIndexUrl2 = "https://antenati.cultura.gov.it/search-registry/?localita={localita}";
        public static string getIndexUrl2(string idSerie) {
            return SeriesIndexUrl2.Replace("{localita}", Uri.EscapeDataString(Serie.serieById[idSerie].localita ?? Serie.serieById[idSerie].description));
        }
        public List<IArchiveNode> explore() {
            return AnnoSerie.read(key);
        }

        [JsonIgnore]
        public List <AnnoSerie> myAnnoSeries = new List<AnnoSerie>();

        [JsonIgnore]
        public string href { get { return $"https://antenati.cultura.gov.it/search-registry/?serie={idSerie}"; } }

        public string localita { get; set; }

        public string description { get; set; }

        [JsonIgnore]
        public string idArchivio { get; set; }

        public string idFondo { get; set; }
        public string idSerie { get; set; }
        [JsonIgnore]
        public string key => idSerie;
        [JsonIgnore]
        public string parentKey => idFondo;
        [JsonIgnore]
        public IArchiveNode parentElement => Fondo.fondoById[parentKey];

        public static void Load(string filePath = SeriesName) {
            // Leggi il contenuto del file JSON
            string json = File.ReadAllText(filePath);

            // Converte la stringa JSON in una lista di oggetti Registro
            series = JsonConvert.DeserializeObject<List<Serie>>(json);
            List<Serie> clean = new List<Serie>();
            foreach (Serie  s in series) {
                if (!serieById.ContainsKey(s.idSerie)) {
                    serieById[s.idSerie] = s;
                    clean.Add(s);
                }
            }

            series = clean;


        }
        static string getIndexUrl(string idArchivio) {
            return SeriesIndexUrl.Replace("{idArchivio}", idArchivio);
        }

        public static void Save(string filePath = SeriesName) {
            // Converte la lista in una stringa JSON
            string json = JsonConvert.SerializeObject(series, Formatting.Indented);

            // Salva il JSON nel file specificato
            File.WriteAllText(filePath, json);
        }
        

        public static List<IArchiveNode> read(string idFondo) {
            Fondo f = Fondo.fondoById[idFondo];
            List<IArchiveNode> res = new List<IArchiveNode>();

            
            var htmlContent = PageLoader.getPage(getIndexUrl(f.idArchivio));
            // Trova il nome dei fondi
            /*  <ul class="flex-list">
             * <!--{"q":"+model_s:\"fondo\" +livello_superiore_i:idFondo","rows":"9999","wt":"json"}-->
             *  <li><a href="/search-registry/?serie=310644&descrizione=Accettura" title="">Accettura</a></li>
             *  <li><a href="/search-registry/?serie=311489&descrizione=Alianello (oggi Aliano)" title="">
             *  ..
             *  </ul>
             */

            string pattern =   $@"<ul class=""flex-list"">\s*<!--\{{""q"":""\+model_s:\\\""fondo\\\"" \+livello_superiore_i:{idFondo}.*?-->\s*([\s\S]*?)<\/ul>";
            Regex regex = new Regex(pattern);

            var match = regex.Match(htmlContent);
            if (match.Success) {
                // Estrarre il contenuto tra i tag <ul> ... </ul> con livello_superiore_i:idFondo
                string ulContent = match.Groups[1].Value;

                // Estrarre ogni serie e descrizione dai singoli <li>
                //string liPattern = @"<li><a href=""/search-registry/\?serie=(\d+)&descrizione=([^""]+)""[^>]*>(.*?)<\/a><\/li>";
                //Regex liRegex = new Regex(liPattern);
                string liPattern = @"<li><a href=""/search-registry/\?serie=(\d+)&descrizione=([^""]+)""[^>]*>\s*(.*?)\s*<\/a><\/li>";
                Regex liRegex = new Regex(liPattern, RegexOptions.Singleline);


                foreach (Match liMatch in liRegex.Matches(ulContent)) {
                    string idSerie = liMatch.Groups[1].Value;
                    string descrizione = liMatch.Groups[2].Value.Trim();
                    Serie S = new Serie(idFondo, idSerie,  descrizione);
                    if (!serieById.ContainsKey(idSerie)) {
                        series.Add(S);
                        serieById[idSerie] = S;
                    }
                    else {
                        S  = serieById[idSerie];
                    }
                    res.Add(S);
                }
            }
            return res;

        }

        static Serie() {
            if (File.Exists(SeriesName)) {
                Load();
            }
            
        }


        public Serie(string idFondo, string idSerie,  string description) {

            this.idSerie = idSerie;
            this.description = description;
            Fondo f = Fondo.fondoById[idFondo];
            this.idFondo = f.idFondo;
            this.idArchivio = f.idArchivio;
            
        }
    }
}
