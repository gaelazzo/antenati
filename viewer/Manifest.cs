using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Policy;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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

		public Dictionary<string, int> pageWidth {get; set;}

		/// <summary>
		/// Livelli del contesto archivistico, separati da ">" (spazi attorno non obbligatori)
		/// </summary>
		private static string[] ContestoParts(string contesto) {
            if (string.IsNullOrWhiteSpace(contesto))
                return new string[0];
            return contesto.Split('>').Select(p => p.Trim()).Where(p => p != "").ToArray();
        }

		/// <summary>
		/// Dalla stringa del contesto archivistico estrae il nome del comune
		/// </summary>
		/// <param name="contesto">es.  //"Archivio di Stato di Bari > Stato civile italiano > Carbonara"</param>
		/// <returns>in questo caso Carbonara; null se il contesto si ferma al fondo</returns>
		private static string GetComuneFromContestoArchivistico(string contesto) {
            // "value": "Archivio di Stato di Napoli > Stato civile della restaurazione (quartieri di Napoli) > Arenella"
            // Il comune e' l'ultimo elemento, in questo caso Arenella, ma solo se sotto il fondo c'e' un livello:
            // con "Archivio > Fondo" l'ultimo elemento sarebbe il fondo.
            var parts = ContestoParts(contesto);
            return parts.Length >= 3 ? parts[parts.Length - 1] : null;
        }

        /// <summary>
        /// Dalla stringa del contesto archivistico estrae il nome del registro
        /// </summary>
        /// <param name="contesto">es.  //"Archivio di Stato di Bari > Stato civile italiano > Carbonara"</param>
        /// <returns>in questo caso Stato civile italiano</returns>
        private static string GetRegistroFromContestoArchivistico(string contesto) {
            // "value": "Archivio di Stato di Napoli > Stato civile della restaurazione (quartieri di Napoli) > Arenella"
            // Il fondo e' sempre il secondo livello
            var parts = ContestoParts(contesto);
            return parts.Length >= 2 ? parts[1] : null;
        }

        private static string GetMetadataValue(JArray metadata, string label) {
            if (metadata == null)
                return null;
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

        // LoadManifest gira sia dall'esplorazione (thread di pool) sia dall'interfaccia (selezione di un registro).
        // Il lock copre solo gli accessi al dizionario, non il download: dentro un lock non si puo' fare await.
        static readonly object allManifestLock = new object();

        static bool tryGetLoaded(string manifestId, out Manifest manifest) {
            lock (allManifestLock) {
                return allManifest.TryGetValue(manifestId, out manifest);
            }
        }

        /// <summary>
        /// Registra il manifest caricato. Se nel frattempo un altro thread ha caricato lo stesso manifest
        /// restituisce quello, cosi' tutti usano la stessa istanza.
        /// </summary>
        static Manifest remember(string manifestId, Manifest manifest) {
            lock (allManifestLock) {
                if (allManifest.TryGetValue(manifestId, out Manifest known))
                    return known;
                allManifest[manifestId] = manifest;
                return manifest;
            }
        }

		async Task GetAllImageSizesAsync(IEnumerable<string> ids) {
			var tasks = ids.Select(ManifestLoader.LoadInfoJsonAsync);
			var results = await Task.WhenAll(tasks);
            foreach (var r in results) {
				Console.WriteLine($"{r.idImage}: {r.width}x{r.height}");
                pageWidth[r.idImage] = r.width;

			}
				
		}

		public static async Task<Manifest> LoadManifest(string manifestId) {
            if (tryGetLoaded(manifestId, out Manifest loaded)) {
                return loaded;
            }
            string filename = manifestFileName(manifestId);
            if (File.Exists(filename)) {
                string json = File.ReadAllText(filename);
                var manifest = JsonConvert.DeserializeObject<Manifest>(json);
                manifest.pageDecode = new Dictionary<string, int>();
                foreach (var item in manifest.mapPage) {
                    manifest.pageDecode[item.Value]= item.Key;
                }
                return remember(manifestId, manifest);
            }

            //manifest address:
            // https://dam-antenati.cultura.gov.it/antenati/containers/registro/manifest
            JObject manifestJson = await ManifestLoader.LoadManifestJsonAsync(manifestId);
            if (manifestJson == null) {
                return null;
            }
            Manifest m = fromJson(manifestJson);

            // Se durante il download un altro thread ha gia' caricato e salvato lo stesso manifest,
            // si usa quello senza riscrivere il file
            if (tryGetLoaded(manifestId, out loaded)) {
                return loaded;
            }

            //await m.GetAllImageSizesAsync(imageIds);
			string jsonData = JsonConvert.SerializeObject(m, Formatting.Indented);
            SafeFile.WriteAllText(filename, jsonData);
            return remember(manifestId, m);
        }

        /// <summary>
        /// Costruisce il Manifest (mappa pagine, larghezze, metadati) dal JSON IIIF scaricato
        /// </summary>
        public static Manifest fromJson(JObject manifestJson) {
            Dictionary<int, string> mapPage = new Dictionary<int, string>();
            Dictionary<string, int> pageDecode = new Dictionary<string, int>();
            Dictionary<string, int> pageWidth= new Dictionary<string, int>();

			// Naviga verso l'array "canvases" all'interno di "sequences"
			var canvases = manifestJson["sequences"]?[0]?["canvases"];
            List<string> imageIds = new List<string>();
			Manifest m = new Manifest();
            if (canvases != null) {
                int pageNumber = 0;
                foreach (var canvas in canvases) {
                    pageNumber += 1;
                    //Estrae il codice del registro an_uaXXXXXXX
                    string idImage = (string)canvas["@id"];
                    

					var pieces = idImage.Split('/');

                    //m.width = canvas["width"]?.ToString();
                    //m.height = canvas["height"]?.ToString();

					var idRegistro =pieces[5];
                    m.idRegistro = idRegistro.Substring(5);
                    var imageCode = pieces[6];

                    mapPage[pageNumber] = imageCode;
					imageIds.Add(imageCode);
					pageDecode[imageCode] = pageNumber;
                    pageWidth[imageCode] = (int)(canvas["width"] ?? 1000);

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
            m.pageWidth = pageWidth;
            m.pageDecode = pageDecode;
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
            return ExtractManifestIdFromHtml(html);
        }

        public static string ExtractManifestIdFromHtml(string html) {
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

        // Base del server dei manifest (modificabile solo dai test, per puntare a un server locale)
        public static string damBaseUrl = "https://dam-antenati.cultura.gov.it/antenati/containers/";

        public static async Task<JObject> LoadManifestJsonAsync(string manifestId) {
            // Costruisce l'URL del manifest del registro
            string url = $"{damBaseUrl}{manifestId}/manifest";


            //https://dam-antenati.cultura.gov.it/antenati/containers/wrrkrG8/manifest
            
            try {
                PageLoader.WaitWarmUp();
                Dictionary <string,string>cookies = new Dictionary<string,string>();
                cookies.Add("PHPSESSID", PageLoader.getCookie("PHPSESSID"));
                cookies.Add("path", "/");

                Dictionary<string, string> headers = new Dictionary<string, string>();
                //headers.Add(":authority", "dam-antenati.cultura.gov.it");
                //headers.Add(":method", "GET");
                //headers.Add( ":path", $"/antenati/containers/{manifestId}/manifest");
                //headers.Add(":scheme", "https");
                headers.Add("Accept", "*/*");
                headers.Add("Accept-Encoding", "gzip, deflate");
                headers.Add("Accept-Language", "it,en-US;q=0.9,en;q=0.8");
                headers.Add("Origin", "https://antenati.cultura.gov.it");
                headers.Add("Priority", "u=1, i");
                headers.Add("Referer", "https://antenati.cultura.gov.it");

                headers.Add("Sec-Ch-Ua", @"""Chromium"";v=""142"", ""Google Chrome"";v=""142"", ""Not?A_Brand"";v=""99""");
                headers.Add("Sec-Ch-Ua-Mobile", "?0");
                headers.Add("Sec-Ch-Ua-Platform", "\"Windows\"");
                headers.Add("Sec-Fetch-Dest", "empty");
                headers.Add("Sec-Fetch-Mode", "cors");
                headers.Add("Sec-Fetch-Site", "same-site");
                //headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36");

				


				// Effettua una richiesta GET per ottenere il JSON
				HttpResponseMessage response = PageLoader.getResponseMessage(url,HttpCompletionOption.ResponseContentRead, headers, cookies);
                response.EnsureSuccessStatusCode(); // Lancia un'eccezione se lo stato non è 200 OK

                Console.WriteLine(response.Content.Headers.ContentEncoding.FirstOrDefault());

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

		public class IiifInfo {
            public string idImage { get; set; }
			public int width {
				get; set;
			}
			public int height {
				get; set;
			}
		}
		public static async Task<IiifInfo> LoadInfoJsonAsync(string idImage) {
			// Costruisce l'URL del manifest del registro  
			string url = $"https://iiif-antenati.cultura.gov.it/iiif/2/{idImage}/info.json";


			//https://dam-antenati.cultura.gov.it/antenati/containers/wrrkrG8/manifest

			try {
				PageLoader.WaitWarmUp();
				Dictionary<string, string> cookies = new Dictionary<string, string>();
				cookies.Add("PHPSESSID", PageLoader.getCookie("PHPSESSID"));
				cookies.Add("path", "/");

				Dictionary<string, string> headers = new Dictionary<string, string>();
				//headers.Add(":authority", "dam-antenati.cultura.gov.it");
				//headers.Add(":method", "GET");
				//headers.Add( ":path", $"/antenati/containers/{manifestId}/manifest");
				//headers.Add(":scheme", "https");
				headers.Add("Accept", "*/*");
                headers.Add("Accept-Encoding", "gzip, deflate");
                headers.Add("Accept-Language", "it,en-US;q=0.9,en;q=0.8");
				headers.Add("Origin", "https://antenati.cultura.gov.it");
				headers.Add("Priority", "u=1, i");
				headers.Add("Referer", "https://antenati.cultura.gov.it");

				headers.Add("Sec-Ch-Ua", @"""Chromium"";v=""142"", ""Google Chrome"";v=""142"", ""Not?A_Brand"";v=""99""");
				headers.Add("Sec-Ch-Ua-Mobile", "?0");
				headers.Add("Sec-Ch-Ua-Platform", "\"Windows\"");
				headers.Add("Sec-Fetch-Dest", "empty");
				headers.Add("Sec-Fetch-Mode", "cors");
				headers.Add("Sec-Fetch-Site", "same-site");
				//headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36");




				// Effettua una richiesta GET per ottenere il JSON
				HttpResponseMessage response = PageLoader.getResponseMessage(url, HttpCompletionOption.ResponseContentRead, headers, cookies);
				response.EnsureSuccessStatusCode(); // Lancia un'eccezione se lo stato non è 200 OK

				// Legge il contenuto della risposta come stringa
				string jsonContent = await response.Content.ReadAsStringAsync();

				// Converte la stringa JSON in un oggetto JObject
				JObject registroJson = JObject.Parse(jsonContent);
				return new IiifInfo() {
					idImage= idImage,
                    width= registroJson["width"]?.Value<int>() ?? 0,
                    height= registroJson["height"]?.Value<int>() ?? 0
				};
			}
			catch (HttpRequestException e) {
				MessageBox.Show($"Errore nella richiesta HTTP ({url}): {e.Message}", "Errore");
				return null;
			}
			catch (Exception e) {
				MessageBox.Show($"Errore generico nella richiesta HTTP ({url}): {e.Message}", "Errore");
				return null;
			}
		}

	}

}
