using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using System.IO;
using Newtonsoft.Json;
using System.Runtime.Remoting.Messaging;

using System.Windows.Forms;


namespace viewer {
    public class Archivio :IArchiveNode {
        public const string archiveName = "Archivi.json";

        public static List<Archivio> archivi = new List<Archivio>();
        public static Dictionary<string, Archivio> archivioById = new Dictionary<string, Archivio>();

        const string ArchivioIndexUrl= "https://antenati.cultura.gov.it/esplora-gli-archivi/";
        public string description { get; set; }
        public string idArchivio { get; set; }

        [JsonIgnore]
        public string href { get { return $"https://antenati.cultura.gov.it/archive/?archivio={idArchivio}"; } }
        
        [JsonIgnore]
        public tipoNodo tipo { get{ return tipoNodo.Archivio; } }
        [JsonIgnore]
        public string key { get { return idArchivio; } }
        [JsonIgnore]
        public IArchiveNode parentElement { get { return null; } }
        [JsonIgnore]
        public string parentKey => null ;

        /// <summary>
        /// Read all childs
        /// </summary>
        /// <returns></returns>
        public List<IArchiveNode> explore() {
            return Fondo.read(key);
        }

        public static void Load(string filePath = archiveName) {
            // Leggi il contenuto del file JSON
            string json = File.ReadAllText(filePath);

            // Converte la stringa JSON in una lista di oggetti Registro
            archivi = JsonConvert.DeserializeObject<List<Archivio>>(json);
            foreach(Archivio a in archivi) {
                archivioById[a.idArchivio] = a;
            }
        }


        public static void Save(string filePath= archiveName) {
            // Converte la lista in una stringa JSON
            string json = JsonConvert.SerializeObject(archivi, Formatting.Indented);

            // Salva il JSON nel file specificato
            File.WriteAllText(filePath, json);
        }

        public static void read(string dummy) {
            archivi = new List<Archivio>();
            //var w = new WebClient();
            //w.Encoding = Encoding.UTF8;
            //var htmlContent = w.DownloadString(ArchivioIndexUrl);
            var htmlContent = PageLoader.getPage(ArchivioIndexUrl);
            // Trova il nome dell'archivio (decodificato)
            /*
             * .setContent("<div>                          <h3>Archivio di Stato di Grosseto</h3>                          
             *          <a href='https://antenati.cultura.gov.it/archivio/archivio-di-stato-di-grosseto/'>Scopri l&#039;archivio</a>
             *          <a href='/archive/?archivio=236&#038;lang=it'>Esplora i fondi</a>
             *          <a href='/search-registry/?archivio=236&#038;descrizione=Archivio di Stato di Grosseto&#038;lang=it'>Cerca nei registri</a></div>");                
             */
            //string pattern = @"\.setContent\(""<div>\s*<h3>(.*?)<\/h3>.*?href='(https:\/\/[^']+)'.*?href='\/archive\/\?archivio=(\d+)";
            //string pattern = @"\.setContent\(""<div>\s*<h3>\s*(.*?)\s*<\/h3>.*?href='(https:\/\/[^']+)'.*?href='\/archive\/\?archivio=(\d+)&amp;";
            //string pattern = @"\.setContent\(""<div>\s*<h3>([^<]+)<\/h3>.*?href='(https:\/\/[^']+)'.*?\/archive\/\?archivio=(\d+)&";
            //string pattern = @"\.setContent\(""<div>\s*<h3>\s*(.*?)\s*<\/h3>.*?href='(https:\/\/[^']+)'[^>]*>.*?\/archive\/\?archivio=(\d+)&";
            //string pattern = @"\.setContent\(""<div>\s*<h3>\s*(.*?)\s*<\/h3>.*?href='(https:\/\/[^']+)'.*?href='\/archive\/\?archivio=(\d+)&.*?>Esplora i fondi";
            //string pattern = @"\.setContent\(""<div>\s*<h3>\s*(.*?)\s*<\/h3>.*?href='(https:\/\/[^']+?)'>.*?href='\/archive\/\?archivio=(\d+)&.*?>Scopri l&#039;archivio";
            //string pattern = @"\.setContent\(""<div>(.*?)>Cerca nei registri</a></div>""\)";
            string pattern = @"\.setContent\(""(.*?)<\/a><\/div>""\)";

            Regex regex = new Regex(pattern, RegexOptions.Singleline);
            var matches = regex.Matches(htmlContent);
            string  pat_h3 = "<h3>(.*?)<\\/h3>";
            Regex regDescription = new Regex(pat_h3, RegexOptions.Singleline);
            string pat_href = "<a\\s+href='(.*?)'>"; // Pattern per catturare il primo href
            Regex regHref = new Regex(pat_href, RegexOptions.Singleline);
            string pat_archivio = @"\?archivio=(\d+)";

            Regex reg_archivio = new Regex(pat_archivio, RegexOptions.Singleline);

            foreach (Match match in matches) {
                // Nome dell'archivio (decodificato)
                string S = match.Groups[0].Value;
                Match match_h3 = regDescription.Match(S);
                string description = HttpUtility.HtmlDecode(match_h3.Groups[1].Value);

                Match matchUrl = regHref.Match(S);
                string url = matchUrl.Groups[1].Value;
                string idArchivio = null;
                Match matchArchivio = reg_archivio.Match(S);
                if (matchArchivio.Success) {
                    idArchivio = matchArchivio.Groups[1].Value;
                    var archivio = new Archivio(idArchivio, url, description);
                    archivi.Add(archivio);
                    archivioById[idArchivio] = archivio;
                }
                Console.WriteLine($"Nome Archivio: {description} URL: {url}, Codice Archivio: {idArchivio ?? "Nessun codice"}");
                
                

                // Visualizza i risultati
                
                
            }
        }

    

        static Archivio() {
            if (File.Exists(archiveName)) {
                Load();
            }
            else {
                read(null);
            }
        }
       
        public Archivio(string idArchivio, string href, string description) {
            this.idArchivio = idArchivio;
            this.description = description;
        }
    }
}
