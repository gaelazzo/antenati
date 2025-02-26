using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.IO;
using Newtonsoft.Json;
using System.Net;
using System.Text;
using System.Runtime.InteropServices;
using System.Security.Policy;
using System.Windows.Forms;

namespace viewer {
    public class Manifest {

        public string idRegistro {  get; set; } 
        public string archivio { get; set; }
        public string nomeRegistroGrezzo { get; set; }
        public string registerName { get; set; }
        public string Comune { get; set; }
        public string Anno { get; set; }
        public string tipologia { get; set; }

        public Dictionary<int, string> mapPage { get; set; }
        [JsonIgnore]
        public Dictionary<string, int> pageDecode { get; set; }

        /// <summary>
        /// Dalla stringa del contesto archivistico estrae il nome del comune
        /// </summary>
        /// <param name="contesto">es.  //"Archivio di Stato di Bari > Stato civile italiano > Carbonara"</param>
        /// <returns>in questo caso Carbonara</returns>
        private static string GetComuneFromContestoArchivistico(string contesto) {
            // "value": "Archivio di Stato di Napoli > Stato civile della restaurazione (quartieri di Napoli) > Arenella"
            // Estrapola il comune, che dovrebbe essere l'ultimo elemento separato da " > ", in questo caso Arenella
            if (contesto != null) {
                var parts = contesto.Split(new string[] { " > " }, StringSplitOptions.None);
                return parts.Length > 0 ? parts[2] : null;
            }
            return null;
        }

        /// <summary>
        /// Dalla stringa del contesto archivistico estrae il nome del registro
        /// </summary>
        /// <param name="contesto">es.  //"Archivio di Stato di Bari > Stato civile italiano > Carbonara"</param>
        /// <returns>in questo caso Stato civile italiano</returns>
        private static string GetRegistroFromContestoArchivistico(string contesto) {
            // "value": "Archivio di Stato di Napoli > Stato civile della restaurazione (quartieri di Napoli) > Arenella"
            // Estrapola il comune, che dovrebbe essere l'ultimo elemento separato da " > "
            if (contesto != null) {
                var parts = contesto.Split(new string[] { " > " }, StringSplitOptions.None);
                return parts.Length > 0 ? parts[1] : null;
            }
            return null;
        }

        private static string GetMetadataValue(JArray metadata, string label) {
            foreach (var item in metadata) {
                if (item["label"]?.ToString() == label) {
                    return item["value"]?.ToString();
                }
            }
            return null;
        }
        public static string manifestFileName(string manifestId) {
            return Path.Combine("manifest", manifestId + ".json");
        }
        static Dictionary<string, Manifest> allManifest = new Dictionary<string, Manifest>();
        public static async Task<Manifest> LoadManifest(string manifestId) {
            if (allManifest.ContainsKey(manifestId)) {
                return allManifest[manifestId];
            }
            string filename = manifestFileName(manifestId);
            if (File.Exists(filename)) {
                string json = File.ReadAllText(filename);
                var manifest = JsonConvert.DeserializeObject<Manifest>(json);
                manifest.pageDecode = new Dictionary<string, int>();
                foreach (var item in manifest.mapPage) {
                    manifest.pageDecode[item.Value]= item.Key;
                }
                allManifest[manifestId] = manifest;
                return manifest;
            }

            //manifest address:
            // https://dam-antenati.cultura.gov.it/antenati/containers/registro/manifest
            JObject manifestJson = await ManifestLoader.LoadManifestJsonAsync(manifestId);
            if (manifestJson == null) {
                return null;
            }                
            Dictionary<int, string> mapPage = new Dictionary<int, string>();
            Dictionary<string, int> pageDecode = new Dictionary<string, int>();

            // Naviga verso l'array "canvases" all'interno di "sequences"
            var canvases = manifestJson["sequences"]?[0]?["canvases"];

            Manifest m = new Manifest();
            if (canvases != null) {
                int pageNumber = 0;
                foreach (var canvas in canvases) {
                    pageNumber += 1;
                    //Estrae il codice del registro an_uaXXXXXXX
                    string idImage = (string)canvas["@id"];
                    var pieces = idImage.Split('/');

                    var idRegistro =pieces[5];
                    m.idRegistro = idRegistro.Substring(5);
                    var imageCode = pieces[6];

                    mapPage[pageNumber] = imageCode;
                    pageDecode[imageCode] = pageNumber;

                    //// Estrai il numero della pagina (label) e il codice URL
                    //string urlCode = (string)canvas["images"]?[0]?["resource"]?["@id"];

                    //if (urlCode != null) {
                    //    // Estrai il codice dall'URL
                    //    string code = urlCode.Split('/')[5];
                    //    mapPage[pageNumber] = code;
                    //    pageDecode[code] = pageNumber;
                    //}
                }
            }

            var metadata = manifestJson["metadata"] as JArray;            
            m.archivio = GetMetadataValue(metadata, "Conservato da");            
            m.nomeRegistroGrezzo = GetMetadataValue(metadata, "Contesto archivistico"); //"Archivio di Stato di Bari > Stato civile italiano > Carbonara"
            m.registerName = GetRegistroFromContestoArchivistico(m.nomeRegistroGrezzo);
            m.Comune = GetComuneFromContestoArchivistico(m.nomeRegistroGrezzo);
            m.Anno = GetMetadataValue(metadata, "Titolo");
            m.tipologia = GetMetadataValue(metadata, "Tipologia");
            m.mapPage = mapPage;

            string jsonData = JsonConvert.SerializeObject(m, Formatting.Indented);
            File.WriteAllText(filename, jsonData);
            m.pageDecode = pageDecode;
            allManifest[manifestId] = m;
            return m;
        }
    }
    public class ManifestIdExtractor {
       

        /// <summary>
        /// Estrae l'id del manifest, che sta nel windowsId della pagina
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        public static string ExtractManifestId(string url) {
            var html = PageLoader.getPage(url);
            

            // Cerca il tag <script> contenente windowsId
            // Usa una regex per estrarre il valore di windowsId
            var match = Regex.Match(html, @"let windowsId\s*=\s*'([^']+)'");
            if (match.Success) {
                 return match.Groups[1].Value;
            }
            

            return null; // Ritorna null se l'ID non è trovato
        }
    }


    public class ManifestLoader {
        private static readonly HttpClient client = new HttpClient();
        

        public static async Task<JObject> LoadManifestJsonAsync(string manifestId) {
            // Costruisce l'URL del manifest del registro
            string url = $"https://dam-antenati.cultura.gov.it/antenati/containers/{manifestId}/manifest";


            //https://dam-antenati.cultura.gov.it/antenati/containers/wrrkrG8/manifest
            
            try {
                Dictionary <string,string>cookies = new Dictionary<string,string>();
                cookies.Add("PHPSESSID", PageLoader.getCookie("PHPSESSID"));
                cookies.Add("path", "/");

                Dictionary<string, string> headers = new Dictionary<string, string>();
                //headers.Add(":authority", "dam-antenati.cultura.gov.it");
                //headers.Add(":method", "GET");
                //headers.Add( ":path", $"/antenati/containers/{manifestId}/manifest");
                //headers.Add(":scheme", "https");
                headers.Add("Accept", "*/*");
                headers.Add("Accept-Encoding", "gzip, deflate, br, zstd");
                headers.Add("Accept-Language", "it,en-US;q=0.9,en;q=0.8");
                headers.Add("Origin", "https://antenati.cultura.gov.it");
                headers.Add("Priority", "u=1, i");
                headers.Add("Referer", "https://antenati.cultura.gov.it");

                headers.Add("Sec-Ch-Ua", @"""Chromium"";v=""130"", ""Google Chrome"";v=""130"", ""Not?A_Brand"";v=""99""");
                headers.Add("Sec-Ch-Ua-Mobile", "?0");
                headers.Add("Sec-Ch-Ua-Platform", "\"Windows\"");
                headers.Add("Sec-Fetch-Dest", "empty");
                headers.Add("Sec-Fetch-Mode", "cors");
                headers.Add("Sec-Fetch-Site", "same-site");
                headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36");

                // Effettua una richiesta GET per ottenere il JSON
                HttpResponseMessage response = PageLoader.getResponseMessage(url,HttpCompletionOption.ResponseContentRead, headers, cookies);
                response.EnsureSuccessStatusCode(); // Lancia un'eccezione se lo stato non è 200 OK

                // Legge il contenuto della risposta come stringa
                string jsonContent = await response.Content.ReadAsStringAsync();

                // Converte la stringa JSON in un oggetto JObject
                JObject registroJson = JObject.Parse(jsonContent);
                return registroJson;
            }
            catch (HttpRequestException e) {
                MessageBox.Show($"Errore nella richiesta HTTP ({url}): {e.Message}","Errore");                
                return null;
            }
            catch (Exception e) {
                MessageBox.Show($"Errore generico nella richiesta HTTP ({url}): {e.Message}", "Errore");                
                return null;
            }
        }
    }

}
