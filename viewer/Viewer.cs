using BitlyAPI;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using Control = System.Windows.Forms.Control;



namespace viewer {




    public partial class Viewer : Form {
        static object hashSemaphore = new object();
        // Download di immagini in corso, uno per codice (sostituisce existingRequests): vedi loadImage
        InFlightDownloads downloads = new InFlightDownloads();
        // Conteggio dei fallimenti di download per codice: serve solo a fermare il retry automatico
        // dopo qualche tentativo (vedi timer1_Tick), NON a blacklistare la pagina in modo permanente.
        // Un successo azzera il contatore (loadImage), quindi ri-navigando si ritenta sempre.
        Dictionary<string, int> failCount = new Dictionary<string, int>();

        string extendCode(string code) {
            return  Regex.Replace(code, "([A-Z])", "_$1");
        }

        /// <summary>
        /// Gets the path for the image of a specified code in the current register
        /// </summary>
        /// <param name="code"></param>
        /// <returns></returns>
        string PathFromCode(string code) {
            if (getCurrRegister() == null)
                return null;
            string fName = code + ".jpg";
            fName = extendCode(fName);
            string fPath = Path.Combine("data",  fName); //getCurrRegister().idRegistro,
			return fPath;
        }

        // savedFiles e' toccato dal caricamento della pagina e dal precaricamento, che girano insieme
        // su thread diversi, e da "Cancella piccoli": va usato sempre sotto hashSemaphore
        void forgetSavedFile(string name) {
            lock (hashSemaphore) {
                savedFiles.Remove(name);
            }
        }

        // Incrementa il contatore di fallimenti del codice. Dopo qualche fallimento il timer
        // smette di ritentare automaticamente (niente martellamento dello stesso URL), ma la
        // pagina NON viene bloccata: ri-navigando si ritenta e un successo azzera il contatore.
        void registerFailure(string code) {
            lock (hashSemaphore) {
                int n = failCount.ContainsKey(code) ? failCount[code] + 1 : 1;
                failCount[code] = n;
                if (n >= 3)
                    Console.WriteLine($"Codice {code} fallito {n} volte: stop al retry automatico (ri-naviga per ritentare).");
            }
        }
        string getImageUrlByCode(string code, bool useMax = false) {
            return IiifServer.current.imageUrl(code, useMax ? 0 : dimension);
        }

        void fillComboArchivi() {
            var rr = Archivio.archivi;
            var bindingSource1 = new BindingSource();
            var bindingSource2 = new BindingSource();
            bindingSource1.DataSource = rr; // rr è la tua lista di registri
            bindingSource2.DataSource = rr;

            cmbArchivio.DisplayMember = "description";
            cmbArchivio.ValueMember = "key";
            cmbArchivio.DataSource = bindingSource1;

            cmbArchivioIndice.ValueMember = "key";
            cmbArchivioIndice.DisplayMember = "description";
            cmbArchivioIndice.DataSource = bindingSource2;
        }

        ToolStripStatusLabel statusLabel;
        Stack<string> stack = new Stack<string>();
        public void pushStatus(string s) {
            stack.Push(s);
            statusLabel.Text = s;
            Application.DoEvents();
        }
        public void popStatus() {

            if (stack.Count > 0) {
                stack.Pop();
            }
            if (stack.Count > 0) {
                var s = stack.Peek();
                statusLabel.Text = s;
            }
            else {
                statusLabel.Text = "Ready";
            }
            Application.DoEvents();

        }



        public Viewer() {
            InitializeComponent();
            wireDimensionBox();
            // Enable double-buffering on picture and panel to reduce flicker during zoom/pan
            try {
                typeof(Control).GetProperty("DoubleBuffered", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(pic, true, null);
                typeof(Control).GetProperty("DoubleBuffered", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(imgPanel, true, null);
                this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
                this.UpdateStyles();
            }
            catch {
            }
			// Cookie di sessione Antenati (PHPSESSID) in background: prima qui c'erano 4 GET sincrone
			// (home, ark, matomo.js, gtag.js) che bloccavano l'apertura della finestra per secondi.
			// matomo e gtag non impostano cookie per Antenati, l'ark ripeteva la home: bastava la home.
			PageLoader.StartWarmUp();



			statusLabel = new ToolStripStatusLabel("Pronto");
            statusStrip.Items.Add(statusLabel);

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
            ServicePointManager.ServerCertificateValidationCallback += ServerCertificateValidationCallback;
            if (!Directory.Exists("data"))
                Directory.CreateDirectory("data");
            fillComboArchivi();


            var files = Directory.EnumerateFiles("data", "*.*", SearchOption.AllDirectories);
            foreach (var f in files) {
                savedFiles.Add(Path.GetFileNameWithoutExtension(f));
            }

            pic.SizeMode = PictureBoxSizeMode.AutoSize;
            initPicEvents();
            canExplore = chkEsplora.Checked;
            readNotes();
            loadAppuntiNodo();
            loadAppuntiCitta();
            loadFamiglieCitta();

            RegNode.fillTree(treeMain.Nodes);
            readStartValues();
            // timer1_Tick non carica pagine finche' txtPageNum ha il focus (l'utente sta scrivendo un
            // numero), e all'apertura il focus finiva proprio li': la pagina iniziale non arrivava
            // finche' non si cliccava altrove. Quando la finestra compare, sposto il focus sull'immagine.
            Shown += (s, e) => { if (txtPageNum.Focused) ActiveControl = imgPanel; };

            InitWebView();
            //btnCalcolaCitta_Click(null, null);
        }

        // Antenati applica hotlink protection sulle immagini IIIF: senza il Referer il
        // server le rifiuta. Inizializziamo la WebView2 e iniettiamo il Referer (e Sec-Fetch-Site)
        // nelle richieste verso iiif-antenati, così "open image in browser" (interno) torna a funzionare.
        private async void InitWebView() {
            try {
                await webView.EnsureCoreWebView2Async();
                webView.CoreWebView2.AddWebResourceRequestedFilter(
                    IiifServer.current.requestFilter,
                    Microsoft.Web.WebView2.Core.CoreWebView2WebResourceContext.All);
                var hotlink = PageLoader.hotlinkHeaders();
                webView.CoreWebView2.WebResourceRequested += (s, ev) => {
                    foreach (var kv in hotlink)
                        ev.Request.Headers.SetHeader(kv.Key, kv.Value);
                };
            }
            catch (Exception ex) {
                Console.WriteLine("InitWebView error: " + ex.Message);
            }
        }

        private int currIndex = 0;
        class RifPage {
            public string rif;
            public string title;
            public RifPage(string rif, string title) {
                this.rif = rif;
                var parts = title.Split('/').ToList();
                if (parts.Count > 2) {
                    parts.RemoveAt(0);
                    parts.RemoveAt(0);
                    title = "../" + String.Join("/", parts.ToArray());
                }
                this.title = title;
            }
            public bool inList(List<RifPage> l) {
                foreach (RifPage rifPage in l) {
                    if (rifPage.rif == rif)
                        return true;
                }
                return false;
            }
            public int indexIn(List<RifPage> l) {
                for (int i = 0; i < l.Count; i++) {
                    var r = l[i].rif;
                    if (r == this.rif)
                        return i;
                }
                return -1;
            }
            public string toString() {
                return rif + "   -    " + title;
            }
        }

        List<RifPage> visited = new List<RifPage>();




        private void PictureBox_MouseWheel(object sender, MouseEventArgs e) {
            // Usa HandledMouseEventArgs per impedire la propagazione
            if (e is HandledMouseEventArgs handledEventArgs) {
                handledEventArgs.Handled = true;
            }

            // Modifica il fattore di zoom in base alla direzione dello scroll
            if (e.Delta > 0)
                zoomStepIn(); // Zoom avanti
            else if (e.Delta < 0)
                zoomStepOut(); // Zoom indietro

        }
        private bool isSelecting = false;
        private bool selectionFreeze = false;
        private Point startPoint;
        private Rectangle selectionRectangle;

        private void PictureBox_Paint(object sender, PaintEventArgs e) {
            if (selectionRectangle != Rectangle.Empty && (isSelecting | selectionFreeze)) {
                using (var pen = new Pen(Color.Red, 2)) {
                    e.Graphics.DrawRectangle(pen, selectionRectangle);
                }
            }
        }
        private void Form_KeyDown(object sender, KeyEventArgs e) {
            if (e.KeyCode == Keys.Escape && (isSelecting || selectionFreeze)) {
                isSelecting = false;
                selectionFreeze = false;
                selectionRectangle = Rectangle.Empty;
                pic.Invalidate(); // Forza il ridisegno per rimuovere il rettangolo
            }
        }

        void initPicEvents() {
            KeyDown += Form_KeyDown; // Aggiungi il gestore per l'evento KeyDown
            KeyPreview = true;

            pic.MouseDown += (ss, ee) => {
                if (ee.Button == MouseButtons.Left) {
                    if ((Control.ModifierKeys & Keys.Shift) == Keys.Shift) { //selection mode
                        isSelecting = true;
                        selectionFreeze = false;
                        startPoint = ee.Location;
                        selectionRectangle = new Rectangle(ee.Location, new Size(0, 0));
                    }
                    else {
                        firstPoint = MousePosition; //drag mode
                    }

                }
            };
            pic.MouseClick += (ss, ee) => {
                if (isSelecting) {
                    if (!selectionFreeze) {
                        selectionFreeze = true;
                        isSelecting = false;
                    }
                    else {
                        isSelecting = false;
                    }
                    pic.Invalidate();
                }
                else {
                    if (selectionFreeze) {
                        selectionFreeze = false;
                        pic.Invalidate();
                    }
                }
            };

            pic.MouseMove += (ss, ee) => {

                if (isSelecting & !selectionFreeze) {
                    var currentPoint = ee.Location;
                    selectionRectangle = new Rectangle(
                        Math.Min(startPoint.X, currentPoint.X),
                        Math.Min(startPoint.Y, currentPoint.Y),
                        Math.Abs(startPoint.X - currentPoint.X),
                        Math.Abs(startPoint.Y - currentPoint.Y));
                    pic.Invalidate();
                    return;
                }

                if (ee.Button == MouseButtons.Left) {
                    Point temp = MousePosition;
                    try {
                        xScroll = imgPanel.HorizontalScroll.Value;
                        xScroll += firstPoint.X - temp.X;
                        if (xScroll < imgPanel.HorizontalScroll.Minimum)
                            xScroll = imgPanel.HorizontalScroll.Minimum;
                        if (xScroll > imgPanel.HorizontalScroll.Maximum)
                            xScroll = imgPanel.HorizontalScroll.Maximum;

                        imgPanel.HorizontalScroll.Value = xScroll;
                    }
                    catch {
                    }

                    try {
                        yScroll = imgPanel.VerticalScroll.Value;
                        yScroll += firstPoint.Y - temp.Y;
                        if (yScroll < imgPanel.VerticalScroll.Minimum)
                            yScroll = imgPanel.VerticalScroll.Minimum;
                        if (yScroll > imgPanel.VerticalScroll.Maximum)
                            yScroll = imgPanel.VerticalScroll.Maximum;

                        imgPanel.VerticalScroll.Value = yScroll;

                    }
                    catch {

                    }

                    firstPoint = temp;
                }
            };



            pic.Paint += PictureBox_Paint;
            pic.MouseWheel += PictureBox_MouseWheel;
        }

        Point firstPoint = new Point();

        private System.Drawing.Image lastImage = null;

        private Dictionary<int, string> mapPage = new Dictionary<int, string>();
		private Dictionary<string, int> pageDecode = new Dictionary<string, int>();

        //https://antenati.cultura.gov.it/ark:/[appCode]/an_ua[registro]/[codepage]

        Manifest currManifest = null;

        string idRegistro = null;

        /// <summary>
        /// Azzera la current image facendo in modo che il prossimo da visitare sia quello nel txt
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnGo_Click(object sender, EventArgs e) {
            // Prima chiamava removeRequest(PathFromCode(code)), che riconvertiva il percorso in percorso
            // e non toglieva mai niente: la ricarica non funzionava e si andava avanti e indietro.
            forceReload(getNum());
        }

        // Precaricamento delle pagine successive: vedi Precacher (3 download insieme, un nuovo avvio
        // sostituisce il precedente). Prima: una pagina alla volta, e se un precaricamento era in corso
        // (es. del registro appena lasciato) quello nuovo non partiva.
        Precacher precacher = new Precacher();

        private int cacheSize = 10;

        void startPrecache(int start, bool forward) {
            var map = mapPage;
            if (map == null)
                return;
            cacheSize = 10;
            if (txtCacheSize.Text != "") {
                int.TryParse(txtCacheSize.Text, out cacheSize);
            }
            int step = forward ? calcStep() : -calcStep();
            if (step == 0)
                step = forward ? 1 : -1;
            var codes = new List<string>();
            for (int i = 0, p = start; i < cacheSize && map.TryGetValue(p, out string c); i++, p += step)
                codes.Add(c);
            precacher.Start(codes, loadImage);
        }

        /// <summary>Numero della pagina che il precaricamento sta scaricando, 0 se fermo</summary>
        int currPrecaching {
            get {
                string code = precacher.Current;
                return code != null && pageDecode != null && pageDecode.TryGetValue(code, out int n) ? n : 0;
            }
        }



		/// <summary>
		/// Carica una immagine in base al suo numero nella sequenza
		/// </summary>
		/// <param name="num"></param>
		/// <returns></returns>
		async Task<string> loadImage(int num) {
            if (!mapPage.ContainsKey(num)) {
                return null;
            }
            return await loadImage(mapPage[num]);
        }

        void deleteCache() {
            if (!Directory.Exists("data"))
                return;
			string[] files = Directory.GetFiles("data", "*.jpg", SearchOption.TopDirectoryOnly);

			foreach (string file in files) {
				try {
					File.Delete(file);
                    forgetSavedFile(Path.GetFileNameWithoutExtension(file));
					Console.WriteLine($"Cancellato: {Path.GetFileName(file)}");
				}
				catch (Exception ex) {
					Console.WriteLine($"Errore durante la cancellazione di {file}: {ex.Message}");
				}
			}
		}

        static Bitmap LoadBitmapUnlocked(string path) {
            // Se il file non esiste proprio non entriamo nel loop di retry: tentare di
            // aprirlo lancerebbe 3 FileNotFoundException "a vuoto" (rumore nel log del
            // debugger: "Eccezione generata: System.IO.FileNotFoundException"). Il
            // chiamante gestisce gia' il ritorno null.
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;

            for (int i = 0; i < 3; i++) {
                try {
                    using (var stream = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite))
                    using (var img = Image.FromStream(stream)) {
                        return new Bitmap(img);
                    }
                }
                catch (FileNotFoundException) {
                    // Sparito tra il File.Exists e l'apertura (race): inutile ritentare.
                    break;
                }
                catch (IOException) {
                    // File temporaneamente bloccato (scrittura concorrente): ritenta.
                    Thread.Sleep(10);
                }
                catch {
                    // Immagine corrotta/non decodificabile: ritentare non aiuta.
                    break;
                }
            }

            return null;
        }

        /// <summary>
        /// Carica l'immagine corrispondente al codice specificato e la aggiunge a savedFiles
        /// </summary>
        /// <param name="code"></param>
        /// <param name="width"></param>
        /// <returns></returns>
        async Task<string> loadImage(string code) {
            if (checkError(code))
                return null;
            if (!pageDecode.ContainsKey(code))
                return null;
            string fPath = PathFromCode(code);
            bool known;
            lock (hashSemaphore) { known = savedFiles.Contains(extendCode(code)); }
            if (known) {
                if (File.Exists(fPath)) {
                    Console.WriteLine($"[{ora()}] File {code} già presente in savedFiles come {extendCode(code)}");
                    return fPath;
                }
                // Voce "fantasma": era in savedFiles ma il file non c'e' piu' su disco
                // (cache svuotata, cancellato a mano, sessione precedente...). Restituire
                // un path inesistente faceva poi fallire LoadBitmapUnlocked. Rimuovo la
                // voce stale e proseguo a ri-scaricare l'immagine.
                Console.WriteLine($"File {code} in savedFiles ma assente su disco: rimuovo voce stale e riscarico");
                forgetSavedFile(extendCode(code));
			}
            else if (File.Exists(fPath) && !downloads.IsInFlight(code)) {
                // su disco ma non registrato (come faceva checkFileRequestExists): lo si usa
                lock (hashSemaphore) { savedFiles.Add(extendCode(code)); }
                return fPath;
            }

            // Un solo download per immagine: se e' gia' in corso (precache, timer) lo si aspetta.
            // Il percorso torna solo quando il file e' davvero su disco, o null se e' fallito/annullato.
            return await downloads.GetOrStart(code, ct => downloadImage(code, fPath, ct));
        }

        /// <summary>Scarica e verifica l'immagine; null se fallita (contata in failCount) o annullata (non contata)</summary>
        async Task<string> downloadImage(string code, string fPath, CancellationToken cancel) {
            try {
                Console.WriteLine($"[{ora()}] Aggiungo in coda richiesta per {code}");
                if (!await queueDownloadFile(code, fPath, false, cancel)) {
                    // un annullamento non e' un fallimento del sito: non deve far abbandonare la pagina
                    if (!cancel.IsCancellationRequested)
                        registerFailure(code);
                    return null;
                }

                var f = new FileInfo(fPath);
                if (f.Length < 10000) {
                    File.Delete(fPath);
                    return null;
                }

                Image img = LoadBitmapUnlocked(fPath);

                if (img == null) {
                    // File scaricato ma non decodificabile (parziale/corrotto): elimina e conta come fallimento
                    try { if (File.Exists(fPath)) File.Delete(fPath); } catch { }
                    registerFailure(code);
                    return null;
                }

                if (img.Height < 100 || img.Width < 100) {
                    // Segnaposto servito dal sito al posto dell'immagine: va cancellato, altrimenti
                    // al giro dopo lo si troverebbe su disco e resterebbe in cache
                    img.Dispose();
                    try { if (File.Exists(fPath)) File.Delete(fPath); } catch { }
                    registerFailure(code);
                    return null;
                }
                img.Dispose();

                lock (hashSemaphore) {
                    savedFiles.Add(extendCode(code));
                    failCount.Remove(code);
                }
                return fPath;
            }
            catch (Exception e) {
                // gira in un thread di background: niente MessageBox (finestre orfane a raffica col sito lento)
                Console.WriteLine($"[{ora()}] {code}: errore in loadImage: {e.GetType().Name}: {e.Message}");
                try { if (File.Exists(fPath)) File.Delete(fPath); } catch { }
                return null;
            }
        }

        /// <summary>
        /// Annulla i download di pagine lontane da page (oltre la finestra di precache) o di un altro
        /// registro. Le pagine vicine restano: tornandoci si riaggancia il download in corso invece
        /// di ripartire da zero. Per forzare una nuova richiesta c'e' forceReload (pulsante Go).
        /// </summary>
        void keepDownloadsNear(int page) {
            var map = mapPage;
            if (map == null)
                return;
            int w = Math.Max(cacheSize, 1) + 1;
            var keep = new HashSet<string>();
            for (int p = page - w; p <= page + w; p++)
                if (map.TryGetValue(p, out string c))
                    keep.Add(c);
            int n = downloads.CancelAllExcept(keep.Contains);
            if (n > 0)
                Console.WriteLine($"[{ora()}] Annullati {n} download di pagine lontane dalla {page}");
        }

        /// <summary>
        /// Ricarica forzata della pagina (pulsante Go): annulla il download in corso, azzera i fallimenti
        /// (dopo 3 il timer smette di ritentare) e fa ripartire il caricamento al tick successivo.
        /// </summary>
        void forceReload(int page) {
            if (mapPage == null || !mapPage.TryGetValue(page, out string code))
                return;
            if (downloads.Cancel(code))
                Console.WriteLine($"[{ora()}] {code}: ricarica forzata, download in corso annullato");
            lock (hashSemaphore) { failCount.Remove(code); }
            nEffettivo = 0;
            currentImage = 0;
        }

        

        int xScroll = 0;
        int yScroll = 0;

		int prevWidth = 0;
		int prevHeight = 0;

        float GetFitZoom(Image img) {
            if (img == null)
                return 1f;

            float zoomX = imgPanel.ClientSize.Width / (float)img.Width;
            float zoomY = imgPanel.ClientSize.Height / (float)img.Height;

            return Math.Min(zoomX, zoomY);
        }


        void displayImage(Image imgBase) {
            if (drawing) {
                Console.WriteLine("skipping because already drawing");
                return;
            }
            drawing = true;

            int baseWidth = imgBase?.Width ?? 0;
            int baseHeight = imgBase?.Height ?? 0;

            Image oldImage = null;

			bool thereWasImage = (lastImage != null);
            if (imgBase != null && lastImage != imgBase) {
				oldImage = lastImage;				 
                lastImage = imgBase;
            }

            if (imgBase == null) {
				lastImage?.Dispose();
                lastImage = null;
				pic.Image = null;
				drawing = false;
                return;
            }

            try {

                if (chkContrast.Checked) {
                    processImage((Bitmap)imgBase, contrastBar.Value);                ///img
                }

                // 1. Calcolo posizione scroll attuale in percentuale
                double lastXRate = 0, lastYRate = 0;

                if (pic.Image != null) {
                    prevWidth = pic.Image.Width;
                    prevHeight = pic.Image.Height;

                    lastXRate = xScroll / (double)prevWidth;
                    lastYRate = yScroll / (double)prevHeight;
                }
                float zoomFactor = GetFitZoom(imgBase) * userZoom;

                // 2. Sostituisco immagine
                Image rendered = imgBase;
                if (zoomFactor != 1.0f) {
                    int w = (int)(imgBase.Width * zoomFactor);
                    int h = (int)(imgBase.Height * zoomFactor);

                    rendered = new Bitmap(imgBase, new Size(w, h));
                }
                var renderWidth = rendered.Width;
                var renderHeight = rendered.Height;

                if (!ReferenceEquals(pic.Image, rendered)) {
                    var old = pic.Image;
                    pic.Image = null;
                    pic.Image = rendered;
                    // Non liberare mai la bitmap base "canonica" (lastImage): verrebbe usata dopo
                    // la dispose al prossimo zoom -> immagine rotta / eccezione GDI+.
                    if (old != null && !ReferenceEquals(old, lastImage) && !ReferenceEquals(old, rendered))
                        old.Dispose();
                    pic.Invalidate();
                    pic.Update();
                }
                else {
                    pic.Refresh();
                }

                // lastImage = rendered;
               
                Console.WriteLine($"[{ora()}] Nuova immagine visualizzata");
				// 3. Ripristino lo scroll
				if (thereWasImage) {
                    xScroll = (int)Math.Round(lastXRate * renderWidth);
                    yScroll = (int)Math.Round(lastYRate * renderHeight);


                    xScroll = Math.Max(imgPanel.HorizontalScroll.Minimum, Math.Min(xScroll, imgPanel.HorizontalScroll.Maximum));
					yScroll = Math.Max(imgPanel.VerticalScroll.Minimum, Math.Min(yScroll, imgPanel.VerticalScroll.Maximum));

                    imgPanel.HorizontalScroll.Value = xScroll;
                    imgPanel.VerticalScroll.Value = yScroll;
                }
                pic.Refresh();

                // Libera la bitmap base precedente: senza questo ogni cambio pagina perdeva una
                // bitmap full-res in memoria nativa GDI -> crescita continua -> instabilita'/crash.
                if (oldImage != null
                    && !ReferenceEquals(oldImage, lastImage)
                    && !ReferenceEquals(oldImage, rendered)
                    && !ReferenceEquals(oldImage, pic.Image))
                    oldImage.Dispose();
            }
            catch (Exception e) {
                string err = e.ToString();
            }
            finally {
                drawing = false;
            }
        }

        bool drawing = false;



        int getNum() {
            string s = txtPageNum.Text.Replace("\"", "").Trim();
            int num = 0;
            bool res = int.TryParse(s, out num);
            return num;
        }

        TreeNode searchByRegistro(string idRegistro, TreeNodeCollection nodes = null) {
            if (nodes == null)
                nodes = treeMain.Nodes;
            foreach (TreeNode n in nodes) {
                RegNode rn = n.Tag as RegNode;
                if (rn != null && rn.tipo == tipoNodo.Registro) {
                    if (rn.key == idRegistro) {
                        return n;
                    }
                }

                var found = searchByRegistro(idRegistro, n.Nodes);
                if (found != null) {
                    return found;
                }
            }

            return null;
        }

        void clearRegister() {
            idRegistro = null;
            currManifest = null;
            pageDecode = new Dictionary<string, int>();
            mapPage = new Dictionary<int, string>();
            lock (hashSemaphore) { failCount.Clear(); }
		}
        void setTotPages() {
            txtTotPages.Text = "";

            if (mapPage == null)
                return;
            txtTotPages.Text = mapPage.Count.ToString();

        }
        /// <summary>
        /// Imposta il registro corrente e la mappa di codifica/decodifica
        /// </summary>
        /// <param name="idRegister"></param>
        void setRegister(string idRegister) {
            if (string.IsNullOrEmpty(idRegister)) {
                clearRegister();
                return;
            }


            txtCodiceRegistro.Text = idRegister;
            idRegistro = idRegister;


            // Codice sconosciuto (indirizzo incollato, start.txt di un'altra sessione...): vista vuota.
            // Prima l'indicizzatore lanciava KeyNotFound e il controllo su null arrivava dopo l'uso.
            if (!Registro.registroById.TryGetValue(idRegister, out Registro r)) {
                clearRegister();
                setTotPages();
                return;
            }
            if (r.nPaginaIndice != 0 && r.idRegistroIndice == null) {
                r.idRegistroIndice = r.idRegistro;
            }

            currManifest = r.manifest;
            if (r.manifest == null) {
                r.explore();
            }
            if (r.manifest == null) {
                clearRegister();
                setTotPages();
                return;
            }


            currManifest = r.manifest;
            pageDecode = currManifest.pageDecode;
            mapPage = currManifest.mapPage;

			setTotPages();

            TreeNode tn = RegNode.addArchiveNodeToTree(r, treeMain.Nodes);
            currentImage = 0;
            nEffettivo = 0;
            txtEffettivo.Text = "";
            setImportante();
            txtCode.Text = "";

            if (r.lastPageViewed != "") {
                txtPageNum.Text = r.lastPageViewed;
            }
            if (getNum() > mapPage.Count) {
                txtPageNum.Text = "1";
            }

            this.Text = getNodePath(treeMain.SelectedNode) + "    -    " + getNodeAddress(tn);
            lbCity.Text = getCity(treeMain.SelectedNode) + " " + (r.kind ?? "");
            toPushHistory = true;
        }

        void setNum(int num) {
            if (num > mapPage.Count) {
                num = mapPage.Count;
            }
            if (num < 1) {
                num = 1;
            }
            txtPageNum.Text = num.ToString();
            currentImage = 0;
        }

        int calcStep() {
            int multiplier = 1;
            if (mouseOverBtnMenoUno || mouseOverBtnPiuUno || chkEnableKey.Checked) {
                if ((Control.ModifierKeys & Keys.Shift) == Keys.Shift) {
                    multiplier = multiplier * 10;
                }
                if ((Control.ModifierKeys & Keys.Control) == Keys.Control) {
                    multiplier = multiplier * 5;
                }
            }

            return multiplier * standardStep;
        }

        private void btnPiuUno_Click(object sender, EventArgs e) {
            if (chkSerial.Checked && txtPageNum.Text != txtEffettivo.Text) {
                return;
            }
            int step = calcStep();
            setNum(getNum() + step);


            startPrecache(getNum(), true);
        }

        private void btnMenoUno_Click(object sender, EventArgs e) {
            int step = calcStep();
            setNum(getNum() - step);
            startPrecache(getNum(), false);
        }
        /// <summary>
        /// Take prev visited element from history
        /// </summary>
        /// <returns></returns>
        RifPage popItem() {
            if (currIndex >= visited.Count)
                return null;
            var res = visited[currIndex];
            if (currIndex > 0) {
                currIndex -= 1;
            }
            updateVisited();
            return res;
        }

        /// <summary>
        /// Take next visited element from history
        /// </summary>
        /// <returns></returns>
        RifPage nextItem() {
            if (currIndex >= visited.Count - 1)
                return null;
            currIndex += 1;
            if (currIndex < 0)
                currIndex = 0;
            updateVisited();
            return visited[currIndex];
        }

        private int nExploring = 0;
        Semaphore Sdownload = new Semaphore(5,20);
        //private TreeNode waitingToGo = null;
        private int currentImage = 0;
        private bool inTimer = false;
        private int autoFound = 0;

        void enablePageChange() {
            txtPageNum.ReadOnly = false;
            btnPiuUno.Visible = true;
            btnMenoUno.Visible = true;
            btnFirst.Visible = true;
            btnLast.Visible = true;
        }
        void disablePageChange() {
            txtPageNum.ReadOnly = true;
            btnPiuUno.Visible = false;
            btnMenoUno.Visible = false;
            btnFirst.Visible = false;
            btnLast.Visible = false;
            precacher.Stop();
        }
        void updateRotateRegister() {
            if (idRegistro == null) {
                btnRotateReg.Visible = false;
                return;
            }
            var sel = treeMain.SelectedNode;
            if (sel == null) {
                btnRotateReg.Visible = false;
                return;
            }
            var par = sel.Parent;
            if (par == null) {
                btnRotateReg.Visible = false;
                return;
            }
            if (!btnRotateReg.Visible && par.Nodes.Count > 1) {
                beep(2000, 200);
            }
            btnRotateReg.Visible = par.Nodes.Count > 1;
        }

        private void timer1_Tick(object sender, EventArgs e) {
            if (inTimer)
                return;
            if (txtPageNum.Text.Trim().Contains("http")) {
                txtPageNum.Focus();
                txtUrlToDecode.Text = txtPageNum.Text.Trim();
                txtPageNum.Text = "";
                decodeUrl();
                toPushHistory = true;
                btnAddToStack.Focus();
                return;
            }
            if (txtPageNum.Text.Trim().Contains("#")) {
                GetAppuntiNodo();
                txtPageNum.Focus();
                gotoRegister(txtPageNum.Text);
                toPushHistory = true;
                btnAddToStack.Focus();
                return;
            }
            if (txtPageNum.Focused)
                return;

            if (getCurrRegister() == null) {
                disablePageChange();
                return;
            }
            enablePageChange();


            inTimer = true;
            // try/finally: un'eccezione a meta' tick lasciava inTimer = true per sempre e il timer
            // non faceva piu' nulla (nessuna pagina caricata, nessun messaggio nel log).
            try {


            if (webView.Source?.AbsoluteUri != null)
                txtCurrent.Text = webView.Source.AbsoluteUri;

            updateCreaIndiceVisible();

            btnStop.Visible = precacher.IsRunning;
            txtNEsplorazioni.Text = nExploring.ToString(); //richieste in corso

            labExplore.Text = currExploring;
            labExplore.Update();
            labPrecache.Text = currPrecaching == 0 ? "" : currPrecaching.ToString();
            if (currPrecaching == 0)
                labExplore.Text = "";

            var requestedImage = autoFound > 0 ? autoFound : getNum();
            if (requestedImage == autoFound) {
                autoFound = 0;
                setNum(requestedImage);
            }

            // currentImage = "token" della pagina piu' recente richiesta. DEVE essere aggiornato ad
            // OGNI tick, non solo quando parte un nuovo task. Altrimenti, con due click rapidi su
            // avanti, il load gia' in volo per la pagina vecchia (N+1) non viene mai invalidato:
            // resta saved == currentImage e committa la sua immagine come se fosse quella corrente,
            // mentre il numero pagina dice gia' N+2. Rinfrescandolo qui, il task in volo diventa
            // stale (saved != currentImage) e non committa nulla; al tick dopo si carica N+2.
            if (requestedImage != 0)
                currentImage = requestedImage;

            // Cambiata pagina o registro: via i download di pagine ormai lontane (vedi keepDownloadsNear)
            if (requestedImage != 0 && (requestedImage != lastKeptPage || !ReferenceEquals(mapPage, lastKeptMap))) {
                lastKeptPage = requestedImage;
                lastKeptMap = mapPage;
                keepDownloadsNear(requestedImage);
            }

            // Consuma l'immagine in attesa SOLO se possiamo davvero disegnarla, e segna nEffettivo
            // (= pagina realmente a video, e quindi quando il numero smette di essere rosso) SOLO
            // dopo il disegno effettivo. Se displayImage venisse saltato (drawing in corso) la lascio
            // in coda per il tick successivo invece di perderla disallineando nEffettivo dal video.
            // Va fatto PRIMA di decidere se avviare un caricamento: altrimenti la pagina appena
            // pronta (nEffettivo ancora vecchio) veniva caricata e disegnata una seconda volta.
            var pending = imageToDisplay;
            if (pending != null && !drawing) {
                int pendingNum = imageToDisplayNum;
                imageToDisplay = null;
                displayImage(pending);
                nEffettivo = pendingNum;
                rememberPageShown(pendingNum);
                Console.WriteLine($"[{ora()}] Pagina {pendingNum} a video" +
                                  (mapPage.ContainsKey(pendingNum) ? $" ({mapPage[pendingNum]})" : ""));
                if (toPushHistory) {
                    toPushHistory = false;
                    pushHistory(getMark());
                }
            }

            // Quando avviare il caricamento della pagina richiesta: vedi PageLoadScheduler.
            int staged = imageToDisplay != null ? imageToDisplayNum : 0;
            if (pageLoads.TryBegin(requestedImage, nEffettivo, staged)) {

                //tabControl1.SelectedTab = tabPage1;
                new Task(async () => {
                    string fPath;
                    try {
                        if (mapPage.ContainsKey(requestedImage)) {
                            int saved = requestedImage;
                            startPrecache(saved + standardStep, true);
                            fPath = await loadImage(mapPage[saved]);
                            if (fPath == null) {
                                // La pagina non si e' caricata. NON ri-richiederla ad ogni tick dopo
                                // qualche tentativo: martellare lo stesso URL e' proprio cio' che
                                // induceva il server a rispondere 403 (auto-DoS) e teneva la UI in
                                // loop. Dopo alcuni fallimenti fissiamo nEffettivo e ci fermiamo;
                                // ri-navigando sulla pagina si ritenta da capo. Nessun blacklist
                                // permanente: la pagina resta richiedibile.
                                int fails;
                                lock (hashSemaphore) { failCount.TryGetValue(mapPage[saved], out fails); }
                                if (fails >= 3 && saved == currentImage)
                                    nEffettivo = saved;
                                toMarkImportant = false;
                                return;
                            }

                            try {
                                if (saved == currentImage) {
                                    // Stage immagine + numero pagina. NB: NON impostare qui nEffettivo.
                                    // "Pagina mostrata" va segnata solo DOPO che displayImage l'ha
                                    // davvero disegnata (vedi sotto): segnarla al load farebbe credere
                                    // visualizzata una pagina che potrebbe non arrivare mai a video, e
                                    // il loop si fermerebbe (numero non piu' rosso) sulla pagina sbagliata.
                                    var bmp = LoadBitmapUnlocked(fPath);
                                    if (bmp != null) {
                                        imageToDisplayNum = saved;   // scritto PRIMA
                                        imageToDisplay = bmp;        // pubblicato per ULTIMO
                                    }
                                }
                            }
                            //catch (FileNotFoundException f) {
                            //    //savedFiles.Remove(fPath);
                            //}
                            catch {
                            }

                        }

                    }
                    catch {
                    }
                    finally {
                        // anche dopo il return del ramo "pagina non caricata"
                        pageLoads.End(requestedImage);
                    }

                }).Start();
            }

            // Gestisce i bottoni +1 e -1
            manageBtnPiuUnoMenoUno(requestedImage);

            txtEffettivo.Text = nEffettivo.ToString();
            if (mapPage.ContainsKey(nEffettivo)) {
                txtCode.Text = mapPage[nEffettivo];
            }

            btnAddToStack.BackColor = (getMark().inList(visited) ? Color.Red : Color.LightGreen);


            updateHistory();

            txtEffettivo.BackColor = (nEffettivo != requestedImage) ? Color.LightCoral : txtCode.BackColor;
            if (toMarkImportant && nEffettivo != 0) {
                string address = getNodeAddress((TreeNode)treeMain.Invoke(new Func<TreeNode>(() => treeMain.SelectedNode))) + "/" + (string)txtCode.Invoke(new Func<string>(() => txtCode.Text + "/"));
                if (address == urlRequested) {
                    this.Invoke(new Action(() => setAsImportant()));

                }
                toMarkImportant = false;
            }
            else {
                setImportante();
            }
            }
            catch (Exception ex) {
                Console.WriteLine($"[{ora()}] timer1_Tick: {ex.GetType().Name}: {ex.Message}");
            }
            finally {
                inTimer = false;
            }

        }
        bool mouseOverBtnPiuUno = false;
        bool mouseOverBtnMenoUno = false;

        void manageBtnPiuUnoMenoUno(int requestedImage) {
            if (requestedImage != 0 & mapPage.ContainsKey(requestedImage)) {
                string requestedCode = mapPage[requestedImage];
                var step = calcStep();

                btnPiuUno.Visible = mapPage.ContainsKey(requestedImage + step);
                btnMenoUno.Visible = mapPage.ContainsKey(requestedImage - step);


                var defaultColor = Color.Khaki;
                var selectedColor = defaultColor;
                if ((Control.ModifierKeys & Keys.Shift) == Keys.Shift) { //selection mode
                    selectedColor = Color.DarkOrange;
                }
                if ((Control.ModifierKeys & Keys.Control) == Keys.Control) { //selection mode
                    selectedColor = Color.Salmon;
                }
                if ((Control.ModifierKeys & Keys.Control) == Keys.Control && (Control.ModifierKeys & Keys.Shift) == Keys.Shift) { //selection mode
                    selectedColor = Color.LightGoldenrodYellow;
                }

                bool mousePLus = mouseOverBtnPiuUno || chkEnableKey.Checked;
                bool mouseMinus = mouseOverBtnMenoUno || chkEnableKey.Checked;

                if (checkError(requestedCode, step)) {
                    btnPiuUno.BackColor = Color.Red;
                }
                else {
                    btnPiuUno.BackColor = mousePLus ? selectedColor : defaultColor;
                }

                if (step > 1 && mousePLus) {
                    btnPiuUno.Text = "+" + step.ToString();
                }
                else {
                    btnPiuUno.Text = ">";
                }

                if (step > 1 && mouseMinus) {
                    btnMenoUno.Text = "-" + step.ToString();
                }
                else {
                    btnMenoUno.Text = "<";
                }

                if (checkError(requestedCode, -step)) {
                    btnMenoUno.BackColor = Color.Red;
                }
                else {
                    btnMenoUno.BackColor = mouseMinus ? selectedColor : defaultColor;
                }

            }
            else {
                btnPiuUno.Visible = false;
                btnMenoUno.Visible = false;
            }

        }

        bool checkError(string code, int step = 0) {
            if (pageDecode == null)
                return true;
            if (!pageDecode.ContainsKey(code))
                return true;
            // NB: niente blacklist permanente qui. Un 403 era quasi sempre auto-inflitto dalla
            // raffica di richieste (vedi precache): bloccare il codice "per sempre" nascondeva
            // pagine valide. Il loop e' fermato a monte (timer) dopo qualche tentativo.
            if (step == 0) {
                return false;
            }
            var pageIndex = pageDecode[code];
            if (!mapPage.ContainsKey(pageIndex + step)) {
                return true;
            }
            return false;
        }

        // Caricamenti di pagina avviati dal timer e non ancora finiti
        private readonly PageLoadScheduler pageLoads = new PageLoadScheduler();
        // Pagina e registro (mapPage) per cui keepDownloadsNear e' gia' stato chiamato
        private int lastKeptPage = 0;
        private object lastKeptMap = null;

        private int nEffettivo = 0;


        // imageToDisplay e imageToDisplayNum sono scritti da un thread di background (il Task di
        // load) e letti dal timer (UI thread): volatile per pubblicarli in modo coerente. La coppia
        // va scritta num-PRIMA / image-DOPO e letta image-PRIMA / num-DOPO, cosi' se l'immagine e'
        // non-null il numero abbinato e' gia' valido.
        private volatile Image imageToDisplay = null;
        private volatile int imageToDisplayNum = 0;


        void pushHistory(RifPage rif) {
            string val = rif.toString();
            if (historyList.Items.Count > 0) {
                string last = historyList.Items[historyList.Items.Count - 1] as string;
                if (last.Trim() == val.Trim()) {
                    return;
                }
            }
            historyList.Items.Add(val.Trim());
            historyList.Refresh();
        }
        void updateHistory() {
            while (historyList.Items.Count > 30)
                historyList.Items.RemoveAt(0);
        }

        private void trackZoom_ValueChanged(object sender, EventArgs e) {

            userZoom = trackZoom.Value / 1000f;
            //recalculateCurrentImageWidth(trackZoom.Value);
            //reloadImage();
            //displayImage(lastImage);
            displayImage(lastImage);
        }
        float userZoom = 1.0f;

        /// <summary>
        /// Zoom salvato in start.txt (valore di trackZoom). Stessa scala di trackZoom_ValueChanged
        /// (/1000: prima qui era /100, e se il valore coincideva col default della barra l'evento non
        /// scattava e la prima pagina usciva 10 volte troppo grande). Valori fuori intervallo vengono
        /// riportati ai limiti invece di lanciare l'eccezione che saltava il resto di start.txt.
        /// </summary>
        void applyZoomSetting(string value) {
            if (!int.TryParse(value?.Trim(), out int z))
                return;
            z = Math.Max(trackZoom.Minimum, Math.Min(trackZoom.Maximum, z));
            trackZoom.Value = z;
            userZoom = z / 1000f;
        }
        

        void zoomStepIn() {
            try {
                if (trackZoom.Value + 100 < trackZoom.Maximum) { //&& isZoomAllowed(trackZoom.Value+100
                    trackZoom.Value += 100;
                    //displayImage(null);
                    //recalculateCurrentImageWidth(trackZoom.Value);
                    //reloadImage();
                    //zoomFactor = trackZoom.Value / 1000f;
                    //displayImage(lastImage);

                }
                else {
                    trackZoom.Value = trackZoom.Maximum;
                }

            }
            catch {

            }
        }
        void zoomStepOut() {
            try {
                if (trackZoom.Value - 100 > trackZoom.Minimum) {  //&& isZoomAllowed(trackZoom.Value - 100)

                    trackZoom.Value -= 100;
                    //displayImage(null);
                    //recalculateCurrentImageWidth(trackZoom.Value);
                    //reloadImage();
                    //zoomFactor = trackZoom.Value / 1000f;
                    //displayImage(lastImage);
                }
                else {
                    trackZoom.Value = trackZoom.Minimum;
                }
                    
            }
            catch {

            }
        }
        private void btnZoomOut_Click(object sender, EventArgs e) {
            zoomStepOut();
        }

        private void btnZoomIn_Click(object sender, EventArgs e) {
            zoomStepIn();
        }

        private void saveSelection() {

            // Calcola la scala dell'immagine rispetto al PictureBox
            var image = (Bitmap)pic.Image;
            float scaleX = (float)image.Width / pic.ClientSize.Width;
            float scaleY = (float)image.Height / pic.ClientSize.Height;

            // Calcola le coordinate reali della selezione
            var cropRect = new Rectangle(
                (int)(selectionRectangle.X * scaleX),
                (int)(selectionRectangle.Y * scaleY),
                (int)(selectionRectangle.Width * scaleX),
                (int)(selectionRectangle.Height * scaleY));

            // Crea il ritaglio
            using (var croppedBitmap = new Bitmap(cropRect.Width, cropRect.Height)) {
                using (var g = Graphics.FromImage(croppedBitmap)) {
                    g.DrawImage(image, new Rectangle(0, 0, croppedBitmap.Width, croppedBitmap.Height), cropRect, GraphicsUnit.Pixel);
                }

                // Salva l'immagine
                using (var saveDialog = new SaveFileDialog { Filter = "JPEG Image|*.jpg|PNG Image|*.png|Bitmap Image|*.bmp" }) {
                    if (saveDialog.ShowDialog() == DialogResult.OK) {
                        croppedBitmap.Save(saveDialog.FileName, ImageFormat.Png); // Salva come PNG di default
                    }
                }
            }


        }

        private void saveToolStripMenuItem_Click(object sender, EventArgs e) {
            if (selectionRectangle != Rectangle.Empty) {
                saveSelection();
                return;
            }

            saveFileDialog1.FileName = mapPage[getNum()] + ".jpg";
            DialogResult res = saveFileDialog1.ShowDialog();
            if (res != DialogResult.OK)
                return;
            System.IO.File.Copy(loadImage(getNum()).GetAwaiter().GetResult(), saveFileDialog1.FileName, true);
        }

        HashSet<string> savedFiles = new HashSet<string>();

        private void btnStop_Click(object sender, EventArgs e) {
            precacher.Stop();
            btnStop.Visible = false;
        }



        private void btnNextVisited_Click(object sender, EventArgs e) {
            var newItem = nextItem();
            if (newItem == null) {
                return;
            }
            GetAppuntiNodo();
            pushHistory(getMark());
            if (newItem != null)
                gotoRegister(newItem.rif);

        }


        private void btnPrevVisited_Click(object sender, EventArgs e) {
            var newItem = popItem();
            if (newItem == null) {
                return;
            }
            GetAppuntiNodo();
            pushHistory(getMark());
            var curr = getMark();
            if (newItem.rif == curr.rif)
                newItem = popItem();
            if (newItem != null)
                gotoRegister(newItem.rif);
        }

        private int standardStep = 1;

        static string ora() => DateTime.Now.ToString("HH:mm:ss.fff");

        // Segnali acustici del programma. Spenti solo dai test (Sandbox), che altrimenti suonano a ogni
        // scenario: es. aprire un registro con altri registri nello stesso gruppo (updateRotateRegister).
        public static bool soundsEnabled = true;
        static void beep(int frequency, int duration) { if (soundsEnabled) Console.Beep(frequency, duration); }
        static void beepSystem() { if (soundsEnabled) System.Media.SystemSounds.Beep.Play(); }

        // Il 18/10/2026, col server sotto carico, un'immagine richiedeva 16-77 s e 5 richieste su 16
        // superavano i 100 s del timeout predefinito di .NET. Oltre ~125 s pero' risponde Cloudflare
        // con 524, quindi aspettare di piu' non serve: 150 s. Non readonly: i test lo accorciano.
        static TimeSpan imageTimeout = TimeSpan.FromSeconds(150);

        public Task<bool> queueDownloadFile(string code, string path, bool forced = true) {
            return queueDownloadFile(code, path, forced, CancellationToken.None);
        }

        /// <param name="cancel">annulla l'attesa del semaforo, la richiesta e la copia (pagina lasciata, Go)</param>
        public async Task<bool> queueDownloadFile(string code,  string path, bool forced, CancellationToken cancel) {
            bool acquired = false;
            string folder = Path.GetDirectoryName(path);
            if (!Directory.Exists(folder)) {
                Directory.CreateDirectory(folder);
            }

            string fileName = Path.GetTempPath() + Guid.NewGuid().ToString() + ".jpg";
            // Tempi del download, per capire se la lentezza e' del sito o del programma:
            //  coda   = attesa del semaforo Sdownload (lato programma)
            //  server = dalla richiesta alla risposta completa (elaborazione IIIF + trasferimento)
            //  disco  = copia nel file temporaneo e nella cache data\
            var swTot = System.Diagnostics.Stopwatch.StartNew();
            long msCoda = 0, msServer = 0;
            try {

                if (!forced) {
                    // attesa del posto annullabile: una pagina lasciata non deve restare in coda
                    if (WaitHandle.WaitAny(new WaitHandle[] { Sdownload, cancel.WaitHandle }) != 0) {
                        Console.WriteLine($"[{ora()}] {code}: annullato mentre era in coda");
                        downloadStats?.AddCancelled(code);
                        return false;
                    }
                    acquired = true;
                }
                msCoda = swTot.ElapsedMilliseconds;


                string url =getImageUrlByCode(code);  //  "https://iiif-antenati.cultura.gov.it/iiif/2/LPbBpOd/full/956,/0/default.jpg"
				currExploring = url;
                Console.WriteLine($"[{ora()}] Richiedo file immagine {url}" + (msCoda > 100 ? $" (dopo {msCoda} ms in coda)" : ""));
				Dictionary<string, string> cookies = new Dictionary<string, string>();

				var headers = PageLoader.siteHeaders();

				var swServer = System.Diagnostics.Stopwatch.StartNew();
				var response = PageLoader.getResponseMessage(url, HttpCompletionOption.ResponseContentRead, headers, cookies, imageTimeout, cancel);
				msServer = swServer.ElapsedMilliseconds;

				// Cantaloupe (il server IIIF di Antenati) risponde 403 Forbidden quando la
				// larghezza richiesta supera quella nativa dell'immagine: l'upscaling e'
				// vietato (max_scale=1.0). Capita sulle pagine piu' piccole del registro
				// (tipicamente la copertina/prima pagina, piu' stretta di 3000px). In quel
				// caso ritentiamo con /full/max/ che restituisce sempre la dimensione nativa
				// senza ingrandire, quindi non viene mai rifiutato.
				if (response != null && (int)response.StatusCode == 403 && dimension != 0) {
					Console.WriteLine($"403 a larghezza {dimension}px: ritento a dimensione nativa (full/max) per {code}");
					response.Dispose();
					url = getImageUrlByCode(code, true);
					currExploring = url;
					Console.WriteLine($"[{ora()}] {code}: 403 dopo {msServer} ms, richiedo {url}");
					swServer.Restart();
					response = PageLoader.getResponseMessage(url, HttpCompletionOption.ResponseContentRead, headers, cookies, imageTimeout, cancel);
					msServer += swServer.ElapsedMilliseconds;
				}

				if (cancel.IsCancellationRequested) {
					Console.WriteLine($"[{ora()}] {code}: annullato dopo {msServer} ms");
					downloadStats?.AddCancelled(code);
					response?.Dispose();
					return false;
				}
				if (response == null || !response.IsSuccessStatusCode) {
					Console.WriteLine($"[{ora()}] {code}: HTTP Error {(int?)response?.StatusCode}: {response?.ReasonPhrase} dopo {msServer} ms");
					downloadStats?.Add(code, response == null ? 0 : (int)response.StatusCode, msCoda, msServer);
					return false;
				}
                // Verifica Content-Length se disponibile
                long? expectedLength = response.Content.Headers.ContentLength;
                Console.WriteLine($"Expected size: {expectedLength?.ToString() ?? "unknown"}");

                using (var fs = new FileStream(fileName, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true)) {
                    using (var stream = await response.Content.ReadAsStreamAsync()) {
                        await stream.CopyToAsync(fs, 81920, cancel);
                        await fs.FlushAsync();
                    }
                }

                var f = new FileInfo(fileName);
                Console.WriteLine($"Actual size: {f.Length}");
                if (expectedLength.HasValue && f.Length != expectedLength.Value) {
                    Console.WriteLine($"WARNING: Size mismatch! Expected {expectedLength}, got {f.Length}");
                    return false;
                }

                bool result = false;
                if (f.Length > 10000) {
                    // Verifica che il file non sia corrotto (es. immagine HTML di errore)
                    byte[] header = new byte[Math.Min(100, f.Length)];
                    using (var fs = new FileStream(fileName, FileMode.Open, FileAccess.Read)) {
                        await fs.ReadAsync(header, 0, header.Length);
                    }

                    // Controlla se è HTML (errore) invece di un'immagine
                    string textHeader = System.Text.Encoding.ASCII.GetString(header);
                    if (textHeader.Contains("<html") || textHeader.Contains("<!DOCTYPE")) {
                        Console.WriteLine("Server returned HTML error page instead of image");                      
                    }
                    else {
                        File.Copy(fileName, path, true);
                        result = true;
                    }
                }
                else {
                    Console.WriteLine($"File too small: {f.Length} bytes - {fileName}");
                    // Leggi il contenuto per debug
                    string content = File.ReadAllText(fileName);
                    Console.WriteLine($"Content: {content.Substring(0, Math.Min(200, content.Length))}");
                }

                System.IO.File.Delete(fileName);
                long msTot = swTot.ElapsedMilliseconds;
                Console.WriteLine($"[{ora()}] {code}: {(result ? "OK" : "SCARTATO")} {f.Length / 1024} KB in {msTot} ms " +
                                  $"(coda {msCoda}, server {msServer}, disco {msTot - msCoda - msServer})");
                downloadStats?.Add(code, (int)response.StatusCode, msCoda, msServer);
                if (currExploring == url)
                    currExploring = "";
                return result;
            }
            catch (OperationCanceledException) {
                Console.WriteLine($"[{ora()}] {code}: annullato dopo {swTot.ElapsedMilliseconds} ms");
                downloadStats?.AddCancelled(code);
            }
            catch (Exception e) {
				Console.WriteLine($"[{ora()}] {code}: queueDownloadFile dopo {swTot.ElapsedMilliseconds} ms: {e.Message}");
				downloadStats?.Add(code, 0, msCoda, swTot.ElapsedMilliseconds - msCoda);
			}
            finally {
                if (acquired)
                    Sdownload.Release();
                // il file temporaneo resta se si esce prima della fine (annullamento, errore, dimensione errata)
                try { if (File.Exists(fileName)) File.Delete(fileName); } catch { }
            }

            return false;
        }

        void writeStartValues() {
            StringBuilder s = new StringBuilder();
            s.AppendLine("bitly§" + bitlyCode);
            s.AppendLine("tinyurl§" + tinyUrlCode);
            s.AppendLine("contrast§" + contrastBar.Value);
            if (txtCacheSize.Text != "")
                s.AppendLine("cache§" + txtCacheSize.Text);
            if (txtAddress.Text != "")
                s.AppendLine("address§" + txtAddress.Text);
            s.AppendLine("winSize§" + this.Size.Width + "§" + this.Size.Height);
            s.AppendLine("split§" + this.splitContainer1.SplitterDistance);

            s.AppendLine("zoom§" + trackZoom.Value);
            s.AppendLine("dimension§" + dimension);

            s.AppendLine("registro§" + idRegistro);

            s.AppendLine("lastScrollX§" + xScroll);
            s.AppendLine("lastScrollY§" + yScroll);
            s.AppendLine("mainTab§" + tabControl1.SelectedIndex);
            s.AppendLine("cityTab§" + tabNodo.SelectedIndex);

            s.AppendLine("contrastOn§" + (chkContrast.Checked ? "S" : "N"));
            s.AppendLine("keys§" + (chkEnableKey.Checked ? "S" : "N"));
            if (treeMain.SelectedNode != null) {
                s.AppendLine("mainStart§" + getNodePath(treeMain.SelectedNode));
            }
            if (txtPageNum.Text != "")
                s.AppendLine("pageNum§" + txtPageNum.Text);



            s.AppendLine("xPos§" + this.Location.X);
            s.AppendLine("yPos§" + this.Location.Y);
            if (!String.IsNullOrEmpty(genealogy?.FileName)) {
                s.AppendLine("gedFileName§" + genealogy.FileName);
            }
            s.AppendLine("txtSearchName§" + txtSearchName.Text);




            System.IO.File.WriteAllText("start.txt", s.ToString());
        }

        void gotoRegister(string idRegistro_page) {
            var pieces = idRegistro_page.Split('#');
            var idRegistro = pieces[0];

            txtCodiceRegistro.Text = idRegistro;

            if (Registro.registroById.ContainsKey(idRegistro)) {
                var registro = Registro.registroById[idRegistro];
                cmbArchivio.SelectedValue = registro.idArchivio;
                setRegister(registro.idRegistro);
                var nn = RegNode.addArchiveNodeToTree(registro, treeMain.Nodes);
                if (nn != null) {
                    nn.EnsureVisible();
                    treeMain.SelectedNode = nn;
                }
            }

            if (pieces.Length > 1) {
                txtEffettivo.Text = "";
                txtCode.Text = "";
                txtPageNum.Text = pieces[1].Split(' ')[0].Trim();
                setImportante();
            }
        }


        async Task readStartValues() {
            int x = 0, y = 0;
            if (System.IO.File.Exists("start.txt")) {
                var n = System.IO.File.ReadAllText("start.txt");
                StringReader sr = new StringReader(n);
                while (sr.Peek() > -1) {
                    string line = sr.ReadLine().Trim();
                    if (string.IsNullOrEmpty(line))
                        continue;
                    string[] pieces = line.Split('§');
                    if (pieces[0] == "pageNum") {
                        // NON scaricare qui la pagina: siamo nel costruttore, sul thread della UI, e la
                        // catena di download e' sincrona fino alla risposta del server (.Result in
                        // PageLoader): la finestra non compariva finche' il sito non rispondeva
                        // (decine di secondi col server lento). Basta impostare il numero: la carica
                        // timer1_Tick in background, come ogni altra pagina.
                        txtPageNum.Text = pieces[1];
                    }
                    if (pieces[0] == "mainStart") {
                        string path = pieces[1];
                        if (!string.IsNullOrEmpty(path)) {
                            setNodePath(treeMain, path);
                        }
                    }
                  

                    if (pieces[0] == "mainTab") {
                        string currTab = pieces[1];
                        tabControl1.SelectTab(toIntOrDefault(currTab, 1));
                    }
                    if (pieces[0] == "cityTab") {
                        string currTab = pieces[1];
                        tabNodo.SelectTab(toIntOrDefault(currTab, 1));
                    }

                    if (pieces[0] == "registro") {
                        if (!string.IsNullOrEmpty(pieces[1])) {
                            string idRegistro = pieces[1];
                            gotoRegister(idRegistro);

                        }
                    }

                    if (pieces[0] == "registroindice") {
                        string idRegistroIndice = pieces[1];
                        if (Registro.registroById.ContainsKey(idRegistroIndice)) {
                            txtCodiceRegistroIndice.Text = idRegistroIndice;
                            cmbArchivioIndice.SelectedValue = idRegistroIndice;
                            setRegister(idRegistroIndice);
                        }
                    }

                    if (pieces[0] == "contrast") {
                        contrastBar.Value = Convert.ToInt32(pieces[1]);
                        txtBar.Text = pieces[1];
                    }

                    if (pieces[0] == "cache") {
                        txtCacheSize.Text = pieces[1];
                    }
                    if (pieces[0] == "contrastOn") {
                        chkContrast.Checked = (pieces[1] == "S");
                    }
                    if (pieces[0] == "keys") {
                        chkEnableKey.Checked = (pieces[1] == "S");
                    }
                    if (pieces[0] == "address") {
                        txtAddress.Text = pieces[1];
                    }
                    if (pieces[0] == "zoom") {
                        applyZoomSetting(pieces[1]);
                    }
                    if (pieces[0] == "lastScrollX") {
                        xScroll = Convert.ToInt32(pieces[1]);
                    }

                    if (pieces[0] == "lastScrollY") {
                        yScroll = Convert.ToInt32(pieces[1]);
                    }

                    if (pieces[0] == "winSize") {
                        int xx = Convert.ToInt32(pieces[1]);
                        if (xx < 100) xx = 100;
                        int yy = Convert.ToInt32(pieces[2]);
                        if (yy < 100) yy = 100;
                        this.Size = new Size(xx, yy);
                        //this.splitContainer1.Size= new Size(Convert.ToInt32(pieces[1])-28, Convert.ToInt32(pieces[2])-31);
                    }
                    if (pieces[0] == "dimension") {
                        string d = pieces[1];
                        if (!string.IsNullOrEmpty(d)) {
                            dimension = Convert.ToInt32(d);
                            txtDimensione.Text = dimension.ToString();
                        }
                    }

                    if (pieces[0] == "split") {
                        this.splitContainer1.SplitterDistance = Convert.ToInt32(pieces[1]);
                        //this.splitContainer1.Size= new Size(Convert.ToInt32(pieces[1])-28, Convert.ToInt32(pieces[2])-31);
                    }

                    if (pieces[0] == "xPos") {
                        x = toIntOrDefault(pieces[1]);
                        if (x < 0) x = 100;
                    }
                    if (pieces[0] == "bitly") {
                        bitlyCode = pieces[1];
                    }
                    if (pieces[0] == "tinyurl") {
                        tinyUrlCode = pieces[1];
                    }



                    if (pieces[0] == "yPos") {
                        y = toIntOrDefault(pieces[1]);
                        if (y < 0) y = 100;
                        this.StartPosition = FormStartPosition.Manual;
                        this.Location = new Point(x, y);
                        //this.splitContainer1.Size= new Size(Convert.ToInt32(pieces[1])-28, Convert.ToInt32(pieces[2])-31);
                    }

                    if (pieces[0] == "gedFileName") {
                        readGedFile(pieces[1]);
                    }

                    if (pieces[0] == "txtSearchName") {
                        txtSearchName.Text = pieces[1];
                    }




                }
            }
            
        }

        Dictionary<string, Squeeze> important = new Dictionary<string, Squeeze>();
        void loadImportant() {
            // Leggi il contenuto del file JSON
            if (System.IO.File.Exists("important_squized.json")) {
                string json = System.IO.File.ReadAllText("important_squized.json");
                important = JsonConvert.DeserializeObject<Dictionary<string, Squeeze>>(json);
                return;
            }
            if (System.IO.File.Exists("important.json")) {
                string json = System.IO.File.ReadAllText("important.json");
                var old_important = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                foreach (var pair in old_important) {
                    string key = pair.Key;
                    string _short = pair.Value;
                    string _long = expandUrl(_short);
                    important[key] = new Squeeze(_short, _long);
                }
            }
            // Converte la stringa JSON in una lista di oggetti Registro

        }
        void loadHistory() {
            // Leggi il contenuto del file JSON
            if (!File.Exists("history.txt")) {
                return;
            }
            string s = File.ReadAllText("history.txt");
            historyList.Items.Clear();
            var ss = s.Split('\n');
            foreach (var line in ss) {
                if (line.Trim() == "")
                    continue;
                historyList.Items.Add(line.Trim());
            }



        }






        public class Squeeze {
            public string _short { get; set; }
            public string _long { get; set; }
            public Squeeze(string _short, string _long) {
                this._short = _short;
                this._long = _long;
            }
        }


        void saveImportant() {
            string json = JsonConvert.SerializeObject(important, Newtonsoft.Json.Formatting.Indented);

            // Salva il JSON nel file specificato
            System.IO.File.WriteAllText("important_squized.json", json);
        }
        void saveHistory() {
            // Serializza la lista in JSON
            StringBuilder s = new StringBuilder();
            for (int i = 0; i < historyList.Items.Count; i++) {
                s.AppendLine(historyList.Items[i].ToString());
            }
            SafeFile.WriteAllText("history.txt", s.ToString());
        }


        public void readNotes() {
            if (System.IO.File.Exists("obiettivi.txt")) {
                var n = System.IO.File.ReadAllText("obiettivi.txt");
                txtObiettivi.Text = n;
            }


            if (System.IO.File.Exists("note.txt")) {
                var n = System.IO.File.ReadAllText("note.txt");
                txtGenerali.Text = n;
            }

            if (System.IO.File.Exists("note2.txt")) {
                var n = System.IO.File.ReadAllText("note2.txt");
                txtNotes2.Text = n;
            }

            if (System.IO.File.Exists("indice.txt")) {
                var s = System.IO.File.ReadAllText("indice.txt");
                treeMain.Nodes.Clear();
                //treeMain.Sorted = true;                               
            }
            loadImportant();
            loadHistory();

        }


        Registro getCurrRegister() {
            //var n = treeMain.SelectedNode;
            //if (n?.Tag == null) return null;
            //RegNode rn = n.Tag as RegNode;
            //if (rn.tipo != tipoNodo.Registro)
            //    return null;
            //var r = Registro.registroById[rn.key];
            // return r;
            if (idRegistro == null) {
                return null;
            }
            if (!Registro.registroById.ContainsKey(idRegistro)) {
                return null;
            }
            return Registro.registroById[idRegistro];
        }

        Registro getCurrIndexRegister() {
            var n = treeMain.SelectedNode;
            if (n?.Tag == null)
                return null;
            RegNode rn = n.Tag as RegNode;
            if (rn.tipo != tipoNodo.Registro)
                return null;

            var r = Registro.registroById[rn.key];
            if (string.IsNullOrEmpty(r.idRegistroIndice))
                return null;
            return Registro.registroById[r.idRegistroIndice];
        }

        Dictionary<string, string> appuntiNodo = new Dictionary<string, string>();
        Dictionary<string, string> appuntiCitta = new Dictionary<string, string>();
        Dictionary<string, string> famiglieCitta = new Dictionary<string, string>();

        void SaveTextDictionary(Dictionary<string, string> dic, string fileName) {
            StringBuilder s = new StringBuilder();
            foreach (var app in dic) {
                string content = app.Value.Replace("\r\n", "#§#§");
                content = content.Replace("\n", "#**§");
                s.AppendLine($"{app.Key}§{content}");
            }
            SafeFile.WriteAllText(fileName, s.ToString());
        }
        void saveAppuntiNodo() {
            GetAppuntiNodo();
            SaveTextDictionary(appuntiNodo, "appunti.txt");
        }
        void saveAppuntiCitta() {
            GetAppuntiCitta();
            SaveTextDictionary(appuntiCitta, "appuntiCitta.txt");
        }

        void saveFamiglieCitta() {
            GetFamiglieCitta();
            SaveTextDictionary(famiglieCitta, "famiglieCitta.txt");
        }

        void loadTextDictionary(string fileName, Dictionary<string, string> dic) {
            if (!System.IO.File.Exists(fileName))
                return;
            var s = System.IO.File.ReadAllText(fileName);
            StringReader sr = new StringReader(s);
            while (sr.Peek() > -1) {
                string line = sr.ReadLine().Trim();
                if (string.IsNullOrEmpty(line))
                    return;
                line = line.Replace("#§#§", "\r\n");
                line = line.Replace("#**§", "\n");

                var cc = line.Split('§');
                dic[cc[0]] = cc[1];
            }
        }

        void loadAppuntiNodo() {
            loadTextDictionary("appunti.txt", appuntiNodo);
        }

        void loadAppuntiCitta() {
            loadTextDictionary("appuntiCitta.txt", appuntiCitta);
        }

        void loadFamiglieCitta() {
            loadTextDictionary("famiglieCitta.txt", famiglieCitta);
        }

        public void saveNotes() {
            StringBuilder s = new StringBuilder();
            Archivio.Save();
            Fondo.Save();
            Serie.Save();
            AnnoSerie.Save();
            AnnoSerieKind.Save();
            Registro.Save();

            SafeFile.WriteAllText("indice.txt", s.ToString());
            SafeFile.WriteAllText("note.txt", txtGenerali.Text);
            SafeFile.WriteAllText("note2.txt", txtNotes2.Text);
            SafeFile.WriteAllText("obiettivi.txt", txtObiettivi.Text);
            saveAppuntiNodo();
            saveAppuntiCitta();
            saveFamiglieCitta();
            saveImportant();
            saveHistory();
        }






        const string addrPrefix = "https://antenati.cultura.gov.it/";



        TreeNode getNodeByPath(TreeNodeCollection nodes, List<string> pieces, string fileName) {
            while (pieces.Count >= 2 && pieces[pieces.Count - 1] == pieces[pieces.Count - 2])
                pieces.RemoveAt(pieces.Count - 1);
            int toInsert = 0;
            for (int i = 0; i < nodes.Count; i++) {
                var n = nodes[i];
                //n.Text valore nodo i-mo, pieces[0] valore da inserire
                toInsert = i + 1;

                //se valore nodo i-mo < valore da inserire vai avanti e come minimo inserisci in i+1
                if (string.Compare(n.Text, pieces[0], StringComparison.InvariantCultureIgnoreCase) < 0)
                    continue;

                if (string.Compare(n.Text, pieces[0], StringComparison.InvariantCultureIgnoreCase) == 0) {
                    //se è l'ultimo imposta il tag
                    if (pieces.Count == 1) {
                        return n;
                    }

                    pieces.RemoveAt(0);
                    return getNodeByPath(n.Nodes, pieces, fileName);
                }

                //se valore nodo i-mo > valore da inserire deve inserire in posizione i, prima di quello attuale
                toInsert = i;
                break;
            }

            return null;
        }



        private void Form1_FormClosing(object sender, FormClosingEventArgs e) {
            saveNotes();
            writeStartValues();
            Archivio.Save();
        }

        string getArchive(TreeNode n) {
            string s = getNodePath(n);
            if (s == null)
                return null;
            var pieces = s.Split('/');
            if (pieces.Length < 0)
                return null;
            foreach (var a in Archivio.archivi) {
                if (a.description == pieces[0])
                    return a.idArchivio;
            }
            return null;
        }

        string getCity(TreeNode n) {
            string s = getNodePath(n);
            if (s == null)
                return null;
            var pieces = s.Split('/');
            if (pieces.Length < 3)
                return null;
            return pieces[2];
        }
        bool setNodePath(TreeView tree, string s) {
            if (s == null)
                return false;
            GetAppuntiNodo();
            s = s.TrimEnd('/');
            var parts = s.Split('/');
            TreeNode curr = null;
            int nfound = 0;
            foreach (string part in parts) {
                if (part == "") {
                    break;
                }
                TreeNodeCollection nodes = curr == null ? tree.Nodes : curr.Nodes;
                if (curr != null & nodes.Count == 0) {
                    exploreMainNode(curr);
                    nodes = curr.Nodes;
                }
                bool found = false;
                foreach (TreeNode n in nodes) {
                    if (n.Text == part) {
                        curr = n;
                        nfound++;
                        found = true;
                        break;
                    }
                }
                if (!found)
                    break;
            }
            if (nfound == parts.Length - 1) {
                if (curr != null && curr.Nodes.Count > 0) {
                    curr = curr.Nodes[0];
                    nfound++;
                    beep(200, 50);
                }
            }
            if (curr != null && nfound == parts.Length) {
                tree.SelectedNode = curr;
                curr.EnsureVisible();
                tree.Focus();
                return true;
            }
            return false;

        }
        string getNodePath(TreeNode n) {
            string s = "";
            while (n != null) {
                s = n.Text + "/" + s;
                n = n.Parent;
            }

            return s;
        }
        string getNodeAddress(TreeNode n) {
            var node = n?.Tag as RegNode;
            if (node is null)
                return null;
            return node.archiveNode.href;
        }




        string getNodeAddress2(TreeNode n) {
            var node = n?.Tag as RegNode;
            if (node is null)
                return null;

            if (node.tipo == tipoNodo.Registro) {
                var reg = node.archiveNode as Registro;
                return Registro.getIndexUrl2(reg.idSerie, reg.anno, reg.kind);
            }
            if (node.tipo == tipoNodo.Serie) {
                var reg = node.archiveNode as Serie;
                return Serie.getIndexUrl2(reg.idSerie);
            }
            if (node.tipo == tipoNodo.AnnoSerie) {
                var reg = node.archiveNode as AnnoSerie;
                return AnnoSerie.getIndexUrl2(reg.idSerie, reg.anno);
            }
            return node.archiveNode.href;
        }



        void updateCreaIndiceVisible() {
            var currReg = getCurrRegister();
            btnCreaIndice.Visible = currReg != null;

            if (currReg != null) {
                var currIndex = getCurrIndexRegister();
                if (currIndex != null) {
                    if (currReg.nPaginaIndice != 0) {
                        btnCreaIndice.BackColor = Color.Aqua;
                    }
                    else {
                        btnCreaIndice.BackColor = btnGo.BackColor;
                    }
                }

                var indexAvailable = currIndex != null;
                btnGoToIndex.Visible = indexAvailable;
            }
            else {
                btnGoToIndex.Visible = false;
            }
        }

        void SetTxtTree(TreeNode n) {
            txtIdSerie.Text = "";
            txtIdFondo.Text = "";
            txtIdRegistro.Text = "";
            txtAnnoTree.Text = "";
            lblAnno.Text = txtAnnoTree.Text;
            txtKind.Text = "";
            txtLocalità.Text = "";
            if (n == null) {
                return;
            }
            RegNode rn = n.Tag as RegNode;
            switch (rn.tipo) {
                case tipoNodo.Archivio:
                return;
                case tipoNodo.Fondo: {
                    var fondo = rn.archiveNode as Fondo;
                    txtIdFondo.Text = fondo.idFondo;
                    return;
                }
                case tipoNodo.Serie: {
                    var serie = rn.archiveNode as Serie;
                    txtIdFondo.Text = serie.idFondo;
                    txtIdSerie.Text = serie.idSerie;
                    txtLocalità.Text = serie.localita ?? "";
                    return;
                }
                case tipoNodo.AnnoSerie: {
                    var annoSerie = rn.archiveNode as AnnoSerie;
                    txtIdFondo.Text = annoSerie.idFondo;
                    txtIdSerie.Text = annoSerie.idSerie;
                    txtAnnoTree.Text = annoSerie.anno;
                    lblAnno.Text = txtAnnoTree.Text;
                    txtLocalità.Text = ((Serie)(annoSerie.parentElement)).localita ?? "";
                    return;
                }
                case tipoNodo.AnnoSerieKind: {
                    var annoSerieKind = rn.archiveNode as AnnoSerieKind;
                    txtIdFondo.Text = annoSerieKind.idFondo;
                    txtIdSerie.Text = annoSerieKind.idSerie;
                    txtAnnoTree.Text = annoSerieKind.anno;
                    lblAnno.Text = txtAnnoTree.Text;
                    txtKind.Text = annoSerieKind.kind;
                    txtLocalità.Text = ((Serie)(annoSerieKind.parentElement.parentElement)).localita ?? "";
                    return;
                }
                case tipoNodo.Registro: {
                    var registro = rn.archiveNode as Registro;
                    txtIdFondo.Text = registro.idFondo;
                    txtIdSerie.Text = registro.idSerie;
                    txtAnnoTree.Text = registro.anno;
                    lblAnno.Text = txtAnnoTree.Text;
                    txtKind.Text = registro.kind;
                    txtIdRegistro.Text = registro.idRegistro;
                    txtLocalità.Text = ((Serie)(registro.parentElement.parentElement.parentElement)).localita ?? "";
                    return;
                }
            }
        }
        void fillTxt(TextBox t, Dictionary<string, string> dic, string k) {
            if (k != null) {
                t.Enabled = true;
                if (dic.ContainsKey(k)) {
                    t.Text = dic[k];
                }
                else {
                    t.Text = "";
                }
            }
            else {
                t.Enabled = false;
                t.Text = "";
            }

        }
        private void treeMain_AfterSelect(object sender, TreeViewEventArgs e) {
            clearRegister();
            var node = e.Node as TreeNode;
            txtAddress.Text = getNodeAddress(node);
            txtAddress2.Text = getNodeAddress2(node);
            SetTxtTree(node);
            bool indexAvailable = false;
            if (node != null) {
                cmbArchivio.SelectedValue = getArchive(node);
                string currCity = getCity(node);
                fillTxt(txtCitta, appuntiCitta, currCity);
                fillTxt(txtFamiglie, famiglieCitta, currCity);
                fillTxt(txtNodo, appuntiNodo, getNodePath(node));
                if (currCity != null) {
                    tabPageCitta.Text = currCity;
                    tabPageFamiglie.Text = "Famiglie";
                }
                else {
                    tabPageCitta.Text = "-";
                    tabPageFamiglie.Text = "-";
                }


                if (node.Tag != null) {
                    var currIndex = getCurrIndexRegister();
                    indexAvailable = currIndex != null;

                    if (indexAvailable) {
                        toolTip1.SetToolTip(btnGoToIndex, currIndex.idRegistro);
                    }

                    idRegistro = getIdRegister(node);
                    if (idRegistro != null) {
                        calcolaAnnoInizio();
                        setRegister(idRegistro);
                        TreeNode foundCitta = searchByRegistro(idRegistro, treeViewCitta.Nodes);
                        if (foundCitta != null) {
                            foundCitta.EnsureVisible();
                            if (!_treeSelecting && treeViewCitta.SelectedNode != foundCitta) {
                                _treeSelecting = true;
                                try { treeViewCitta.SelectedNode = foundCitta; }
                                finally { _treeSelecting = false; }
                            }
                        }
                    }

                }
            }


            updateCreaIndiceVisible();
            updateRotateRegister();

            if (treeMain.SelectedNode != null) {
                var regNode = treeMain.SelectedNode.Tag as RegNode;
                if (regNode == null)
                    return;
                var archive = regNode.archiveNode;
                txtWebAddress.Text = archive.href;
                //getNodeAddress(treeMain.SelectedNode).Replace("+", " ").Substring(addrPrefix.Length);
            }

            btnGoToIndex.Visible = indexAvailable;
        }







        private bool _exploring = false;

        // Anti-rientro per la sincronizzazione treeMain <-> treeViewCitta: senza questo, selezionare
        // un nodo in un albero riseleziona l'altro, che riseleziona il primo... -> StackOverflow.
        private bool _treeSelecting = false;

        // Aggiornamento dell'albero: gira sul thread della UI.
        void applyChildren(TreeNode nn, RegNode regN, List<IArchiveNode> nodes) {
            if (regN.archiveNode.tipo == tipoNodo.Registro) {
                setRegister(regN.key);
            }
            foreach (var n in nodes) {
                RegNode.addArchiveNodeToTree(n, treeMain.Nodes);
            }
            nn.Expand();
        }

        // SINCRONO: usato dai navigatori di percorso. Il fetch va OFF-THREAD via Task.Run: bloccare
        // il .Result del fetch sul thread UI stalla sul SynchronizationContext (una GET da ~10ms
        // diventa secondi). Su un thread di pool è immediato.
        void exploreMainNode(TreeNode nn) {
            var regN = nn.Tag as RegNode;
            pushStatus($"Explore {regN.key} {regN.title}");
            labExplore.Text = regN.archiveNode.href;
            labExplore.Update();
            var nodes = Task.Run(() => regN.archiveNode.explore() ?? new List<IArchiveNode>()).GetAwaiter().GetResult();
            labExplore.Text = currExploring;
            popStatus();
            applyChildren(nn, regN, nodes);
        }

        // ASINCRONO: usato dal doppio click. Fetch off-thread + guard anti-rientro (niente
        // esplorazioni in parallelo, che il programma non gestisce).
        async Task exploreMainNodeAsync(TreeNode nn) {
            if (nn == null) return;
            var regN = nn.Tag as RegNode;
            if (regN == null || _exploring) return;
            _exploring = true;
            try {
                pushStatus($"Explore {regN.key} {regN.title}");
                labExplore.Text = regN.archiveNode.href;
                labExplore.Update();
                var nodes = await Task.Run(() => regN.archiveNode.explore() ?? new List<IArchiveNode>());
                labExplore.Text = currExploring;
                applyChildren(nn, regN, nodes);
            }
            finally {
                popStatus();
                _exploring = false;
            }
        }

        private void cancellaToolStripMenuItem_Click(object sender, EventArgs e) {
            if (treeMain.SelectedNode == null)
                return;
            treeMain.SelectedNode.Remove();
        }




        private bool canExplore = false;


        private string currExploring = "-";
        private static object semaforo = new object();



        private void Form1_KeyUp(object sender, KeyEventArgs e) {
            if (!chkEnableKey.Checked)
                return;

            int step = calcStep();
            bool ctrl = e.Control;
            if (!chkSerial.Checked || txtPageNum.Text == txtEffettivo.Text){
                if (e.KeyCode == Keys.Left){
                    setNum(getNum() - step);
                    chkEnableKey.Select();
                }

                if (e.KeyCode == Keys.Right){
                    setNum(getNum() + step);
                    chkEnableKey.Select();
                }

            }

        }

        // Rilevamento doppio click FATTO A MANO. Non possiamo fidarci di NodeMouseDoubleClick:
        // sul doppio click "diretto" (nodo non selezionato) la selezione del 1° click fa scattare
        // AfterSelect, che cambia focus/controlli e AZZERA il contatore di doppio click di Windows
        // -> il 2° click arriva con CLICKS=1 e l'evento doppio click non parte. Contando noi i due
        // click sullo stesso nodo entro la soglia di sistema, il problema sparisce.
        private DateTime _lastDownTime = DateTime.MinValue;
        private TreeNode _lastDownNode = null;
        private Point _lastDownPos = Point.Empty;

        private async void treeMain_MouseDown(object sender, MouseEventArgs e) {
            if (e.Button != System.Windows.Forms.MouseButtons.Left) return;

            TreeNode node = treeMain.GetNodeAt(e.X, e.Y);
            if (node == null) { _lastDownNode = null; return; }

            DateTime now = DateTime.Now;
            System.Drawing.Size dc = SystemInformation.DoubleClickSize;
            bool sameSpot = Math.Abs(e.X - _lastDownPos.X) <= dc.Width && Math.Abs(e.Y - _lastDownPos.Y) <= dc.Height;
            bool inTime = (now - _lastDownTime).TotalMilliseconds <= SystemInformation.DoubleClickTime;

            if (node == _lastDownNode && sameSpot && inTime) {
                _lastDownTime = DateTime.MinValue;   // consuma: evita di interpretare un 3° click come nuovo doppio
                _lastDownNode = null;
                if (treeMain.SelectedNode != node) treeMain.SelectedNode = node;
                await exploreMainNodeAsync(node);
            }
            else {
                _lastDownTime = now;
                _lastDownNode = node;
                _lastDownPos = new Point(e.X, e.Y);
            }
        }

        private void treeMain_MouseMove(object sender, MouseEventArgs e) {
            var n = treeMain.GetNodeAt(e.X, e.Y);
            var tip = n?.Tag as RegNode;
            if (tip == null) {
                toolTip1.Active = false;
                return;
            }

            toolTip1.Active = true;

            string old = toolTip1.GetToolTip(treeMain);
            if (old == tip.key) {
                return;
            }

            toolTip1.SetToolTip(treeMain, tip.key);

        }

        private void btnSave_Click(object sender, EventArgs e) {
            btnSave.Visible = false;
            saveNotes();
            writeStartValues();
            btnSave.Visible = true;
        }

        private void chkEsplora_CheckedChanged(object sender, EventArgs e) {
            lock (semaforo) {
                canExplore = chkEsplora.Checked;
            }
        }

        private void btnOpenBrowser_Click(object sender, EventArgs e) {
            if (txtWebAddress.Text.Trim() == "")
                return;
            webView.Source = new Uri(txtWebAddress.Text, UriKind.Absolute);


        }

        private void btnOpenExternal_Click(object sender, EventArgs e) {
            System.Diagnostics.Process.Start(txtWebAddress.Text);
        }

        void saveNodes(StringBuilder s) {
            foreach (TreeNode n in treeMain.Nodes)
                addNodes(s, n, "");
        }

        void addNodes(StringBuilder s, TreeNode n, string prefix) {
            if (n.Nodes.Count > 0) {
                foreach (TreeNode child in n.Nodes) {
                    addNodes(s, child, prefix + n.Text + "/");
                }
            }
            else {
                string line = prefix + n.Text + "/";
                var rn = n.Tag as RegNode;
                if (rn.tipo != tipoNodo.Registro)
                    return;
                line += rn.key;
                s.AppendLine(line.Replace(" ", "+"));
            }
        }

        private void btnCalcolaCitta_Click(object sender, EventArgs e) {
            btnCalcolaCitta.Visible = false;
            treeViewCitta.BeginUpdate();
            treeViewCitta.Nodes.Clear();
            //da http://dl.antenati.san.beniculturali.it/v/Archivio+di+Stato+di+Bari/Stato+civile+della+restaurazione/Acquaviva/Matrimoni/1816/
            // a Archivio+di+Stato+di+Bari   Acquaviva   Matrimoni   1816
            StringBuilder s = new StringBuilder();
            saveNodes(s);
            StringReader sr = new StringReader(s.ToString());

            while (sr.Peek() > -1) {
                string line = sr.ReadLine().Trim();
                if (string.IsNullOrEmpty(line))
                    break;
                addCity(line);
            }

            treeViewCitta.EndUpdate();
            btnCalcolaCitta.Visible = true;
        }

        /// <summary>
        /// Aggiunge un indirizzo al tree, ed eventualmente i suoi discendenti ove non sia una foglia e autoexplore=true
        /// </summary>
        /// <param name="address">must be in the form Archivio+di+Stato+di+Bari/Stato+civile+italiano/Bari/Cittadinanze/</param>
        /// <param name="autoexplore"></param>
        void addCity(string address) {
            string originalAddress = address;

            //"Archivio+di+Stato+di+Bari/Stato+civile+italiano/Bari/Cittadinanze/"
            if (address.StartsWith(addrPrefix)) {
                address = address.Substring(addrPrefix.Length);
            } //  >>

            var pieces = address.Replace("+", " ").Split('/').ToList();

            if (pieces[pieces.Count - 1] == "") {
                pieces.RemoveAt(pieces.Count - 1);
            }

            string code = pieces[pieces.Count - 1];
            pieces.RemoveAt(pieces.Count - 1);


            if (pieces.Count == 0)
                return;

            string[] city = new string[pieces.Count - 1];
            city[0] = pieces[0];
            for (int i = 1; i < city.Length; i++) {
                city[i] = pieces[i + 1];
            }

            addPathCity(treeViewCitta.Nodes, city.ToList(), code);
        }


        /// <summary>
        /// Aggiunge un nodo al tree e ne invoca la successiva esplorazione  ove non sia foglia e autoexplore=true
        /// </summary>
        /// <param name="nodes"></param>
        /// <param name="pieces"></param>
        /// <param name="fileName"></param>
        /// <param name="autoexplore"></param>
        void addPathCity(TreeNodeCollection nodes, List<string> pieces, string code) {
            while (pieces.Count >= 2 && pieces[pieces.Count - 1] == pieces[pieces.Count - 2])
                pieces.RemoveAt(pieces.Count - 1);
            int toInsert = 0;
            for (int i = 0; i < nodes.Count; i++) {
                var n = nodes[i];
                //n.Text valore nodo i-mo, pieces[0] valore da inserire
                toInsert = i + 1;

                //se valore nodo i-mo < valore da inserire vai avanti e come minimo inserisci in i+1
                if (string.Compare(n.Text, pieces[0], StringComparison.InvariantCultureIgnoreCase) < 0)
                    continue;

                if (string.Compare(n.Text, pieces[0], StringComparison.InvariantCultureIgnoreCase) == 0) {
                    //se è l'ultimo imposta il tag
                    if (pieces.Count == 1) {
                        if (code != null) {
                            Registro r = Registro.registroById[code];
                            RegNode rn = new RegNode(r);
                            n.Tag = rn;
                            n.ToolTipText = r.description;
                        }

                        return;
                    }

                    pieces.RemoveAt(0);
                    addPathCity(n.Nodes, pieces, code);
                    return;
                }

                //se valore nodo i-mo > valore da inserire deve inserire in posizione i, prima di quello attuale
                toInsert = i;
                break;
            }

            var newNode = new TreeNode();
            nodes.Insert(toInsert, newNode);
            newNode.Text = pieces[0];
            if (pieces.Count == 1) {
                if (code != null) {
                    Registro r = Registro.registroById[code];
                    RegNode rn = new RegNode(r);
                    newNode.Tag = rn;
                    newNode.ToolTipText = r.description;
                }

                return;
            }

            pieces.RemoveAt(0);
            addPathCity(newNode.Nodes, pieces, code);

        }

        void goToCode(string code, bool selectTab = true) {
            if (selectTab)
                tabControl1.SelectTab(tabPage1);
            if (pageDecode.ContainsKey(code)) {
                int n = pageDecode[code];
                txtPageNum.Text = n.ToString();
                txtCode.Text = code;
                txtTotPages.Focus();
                currentImage = 0;
            }

        }

        void goToNumber(int n, bool selectTab = true) {
            if (selectTab)
                tabControl1.SelectTab(tabPage1);
            if (n == 0) {
                n = 1;
            }
            if (n > mapPage.Count) {
                n = mapPage.Count;
            }
            txtPageNum.Text = n.ToString();

            if (mapPage.ContainsKey(n)) {
                txtPageNum.Text = n.ToString();
                txtCode.Text = mapPage[n].ToString();
                txtTotPages.Focus();
                currentImage = 0;
                return;
            }




        }



        private void btnCreaIndice_Click(object sender, EventArgs e) {
            var reg = getCurrRegister();
            if (string.IsNullOrEmpty(txtCodiceRegistroIndice.Text) ||
                    string.IsNullOrEmpty(NPaginaIndice.Text)) {
                return;
            }
            reg.idRegistroIndice = txtCodiceRegistroIndice.Text.Trim();
            reg.nPaginaIndice = toIntOrDefault(NPaginaIndice.Text);


        }

        private void btnGoToIndex_Click(object sender, EventArgs e) {
            pushHistory(getMark());
            var registro = getCurrRegister();
            if (registro != null && registro.idRegistroIndice != null) {
                //Codice per impostare mappa idRegistroIndice
                //TODO  
                int N = registro.nPaginaIndice;
                setRegister(registro.idRegistroIndice);
                goToNumber(N);
            }

        }



        string getIdRegister(TreeNode node) {
            var rn = node?.Tag as RegNode;
            if (rn == null)
                return null;
            if (rn.tipo != tipoNodo.Registro)
                return null;
            return rn.key;
        }

        private void treeViewCitta_AfterSelect(object sender, TreeViewEventArgs e) {
            //bool indexAvailable = false;
            if (treeViewCitta.SelectedNode != null) {
                var currNode = treeViewCitta.SelectedNode;
                if (currNode.Tag != null) {
                    var currReg = currNode.Tag as RegNode;
                    var reg = currReg.archiveNode as Registro;

                    TreeNode foundMain = searchByRegistro(reg.idRegistro, treeMain.Nodes);
                    if (foundMain != null) {
                        foundMain.EnsureVisible();
                        if (!_treeSelecting && treeMain.SelectedNode != foundMain) {
                            _treeSelecting = true;
                            try { treeMain.SelectedNode = foundMain; }
                            finally { _treeSelecting = false; }
                        }

                    }
                }
            }

        }
        public int toIntOrDefault(string input, int valore = 1) {
            return int.TryParse(input, out int result) ? result : valore;
        }

        private void txtAddress_DoubleClick(object sender, EventArgs e) {
            txtAddress.SelectAll();
        }


        private void btnBack_Click(object sender, EventArgs e) {
            if (webView.CanGoBack)
                webView.GoBack();
        }

        private void btnForward_Click(object sender, EventArgs e) {
            if (webView.CanGoForward)
                webView.GoForward();
        }

        private void RinominaMenuStrip_Click(object sender, EventArgs e) {
            var n = treeMain.SelectedNode;
            if (n == null)
                return;
            var f = new frmAskData("Nome del nodo", "Nome", n.Text);
            if (f.ShowDialog(this) == DialogResult.OK) {
                n.Text = f.resultValue;
                var rn = n.Tag as RegNode;
                rn.title = n.Text;
            }
        }

        // Menu dell'albero "imposta pagina": l'indice del registro selezionato e' nello stesso registro,
        // a pagina X (come "imposta indice interno" col tasto destro sull'immagine); 0 o vuoto lo toglie
        private void impostaPaginaToolStripMenuItem_Click(object sender, EventArgs e) {
            var n = treeMain.SelectedNode;
            if (n == null)
                return;
            var r = (n.Tag as RegNode)?.archiveNode as Registro;
            string current = r != null && r.idRegistroIndice == r.idRegistro && r.nPaginaIndice != 0 ? r.nPaginaIndice.ToString() : "";

            var f = new frmAskData("Pagina dell'indice interno al registro", "Numero pagina", current);
            if (f.ShowDialog(this) == DialogResult.OK) {
                string msg = setInternalIndexPage(n, toIntOrDefault(f.resultValue));
                if (msg != null)
                    MessageBox.Show(msg, "Pagina indice");
            }
        }

        /// <summary>
        /// Indice interno al registro del nodo, a pagina page (0 = toglie il collegamento).
        /// Prima il numero finiva nel Tag del nodo al posto del RegNode: non lo rileggeva nessuno e il
        /// nodo perdeva l'identita' di registro fino al riavvio.
        /// </summary>
        string setInternalIndexPage(TreeNode n, int page) {
            var r = (n?.Tag as RegNode)?.archiveNode as Registro;
            if (r == null)
                return "Seleziona un registro nell'albero.";
            return setIndexPage(r, r, page);
        }

        // Menu dell'albero "imposta n. pagina indice": pagina dell'indice gia' collegato (quella a cui
        // porta "vai all'indice"); 0 o vuoto toglie il collegamento
        private void impostaIndiceToolStripMenuItem_Click(object sender, EventArgs e) {

            var reg = getCurrRegister();
            var indexReg = getCurrIndexRegister();

            var f = new frmAskData("N. pagina indice associato al nodo", "N. Pagina indice",
                    reg == null || reg.nPaginaIndice == 0 ? "" : reg.nPaginaIndice.ToString());
            if (f.ShowDialog(this) == DialogResult.OK) {
                string msg = setIndexPage(reg, indexReg, toIntOrDefault(f.resultValue));
                if (msg != null)
                    MessageBox.Show(msg, "Pagina indice");
            }

        }

        /// <summary>
        /// Pagina indice del registro: page != 0 la imposta sull'indice collegato (indexReg), 0 toglie il
        /// collegamento. Restituisce un messaggio per l'utente se non si puo' fare, altrimenti null.
        /// Prima la condizione era invertita (un numero cancellava, 0 collegava con pagina 0) e senza
        /// indice collegato andava in NullReferenceException.
        /// </summary>
        static string setIndexPage(Registro reg, Registro indexReg, int page) {
            if (reg == null)
                return "Nessun registro selezionato.";
            if (page == 0) {
                reg.idRegistroIndice = null;
                reg.nPaginaIndice = 0;
                return null;
            }
            if (indexReg == null)
                return "Il registro non ha un indice collegato: collegalo prima con \"crea indice\" o con \"imposta indice interno\" (tasto destro sull'immagine).";
            reg.idRegistroIndice = indexReg.idRegistro;
            reg.nPaginaIndice = page;
            return null;
        }

        bool extractDate(string s, out int anno, out int mese, out int giorno) {
            anno = 0;
            mese = 0;
            giorno = 0;
            if (s.Length != 10)
                return false;
            bool digitPlaced = char.IsDigit(s[0]) & char.IsDigit(s[1])
                                                  & char.IsDigit(s[3]) & char.IsDigit(s[4])
                                                  & char.IsDigit(s[6]) & char.IsDigit(s[7]) & char.IsDigit(s[8]) &
                                                  char.IsDigit(s[9]);
            bool lodashPlaced = (s[2] == '_') && (s[5] == '_');
            if (!digitPlaced)
                return false;
            if (!lodashPlaced)
                return false;
            giorno = Convert.ToInt32(s.Substring(0, 2));
            mese = Convert.ToInt32(s.Substring(3, 2));
            anno = Convert.ToInt32(s.Substring(6, 4));
            return true;
        }

        TreeNode addChild(TreeNode parent, string text, object tag = null) {
            int toInsert = 0;
            var nodes = parent.Nodes;

            for (int i = 0; i < nodes.Count; i++) {
                var n = nodes[i];
                toInsert = i + 1;
                //se valore nodo i-mo < valore da inserire vai avanti e come minimo inserisci in i+1
                if (string.Compare(n.Text, text, StringComparison.InvariantCultureIgnoreCase) < 0)
                    continue;

                if (string.Compare(n.Text, text, StringComparison.InvariantCultureIgnoreCase) == 0) {
                    if (tag != null)
                        n.Tag = tag;
                    return n;
                }

                toInsert = i;
                break;
            }

            TreeNode newNode = new TreeNode(text);
            newNode.Tag = tag;
            nodes.Insert(toInsert, newNode);
            return newNode;
        }

        // Stessa regola di RegNode (prima qui c'era una copia di ParseFirstDate che andava in eccezione
        // sui nomi non-data)
        void rielaboraNomi(TreeNode parent) {
            RegNode.rielaboraNomi(parent);
        }

        private void rielaboraNomiToolStripMenuItem_Click(object sender, EventArgs e) {
            var parent = treeMain.SelectedNode;
            rielaboraNomi(parent);
        }

        // Ultimi download di immagini (tempi, esiti) per la Diagnosi tempi
        DownloadStats downloadStats = new DownloadStats();

        /// <summary>
        /// Cosa misura la Diagnosi tempi nel registro corrente: la pagina a video come "immagine gia'
        /// vista" e due pagine mai scaricate prese dalla fine del registro (fuori dal precaricamento).
        /// </summary>
        ServerProbe.Options buildDiagnosisOptions() {
            var o = new ServerProbe.Options { Width = dimension };
            var r = getCurrRegister();
            if (r?.manifestId != null)
                o.ManifestUrl = ManifestLoader.damBaseUrl + r.manifestId + "/manifest";
            var map = mapPage;
            if (map == null)
                return o;
            int shown = nEffettivo > 0 ? nEffettivo : getNum();
            if (map.TryGetValue(shown, out string viewed))
                o.ViewedCode = viewed;
            o.ColdCodes = map.OrderByDescending(kv => kv.Key)
                .Select(kv => kv.Value)
                .Where(c => c != viewed && !downloads.IsInFlight(c) && !File.Exists(PathFromCode(c) ?? ""))
                .Take(2)
                .ToArray();
            return o;
        }

        private void btnDiagnosi_Click(object sender, EventArgs e) {
            var o = buildDiagnosisOptions();
            btnDiagnosi.Enabled = false;
            txtDiagnosi.Text = "Diagnosi in corso (pochi secondi, piu' a lungo se il server e' lento)...";
            Task.Run(() => ServerProbe.Run(o, downloadStats)).ContinueWith(t => {
                string text = t.IsFaulted ? "Diagnosi non riuscita: " + t.Exception.GetBaseException().Message : t.Result.Text;
                Console.WriteLine(text);
                BeginInvoke(new Action(() => {
                    txtDiagnosi.Text = text;
                    btnDiagnosi.Enabled = true;
                }));
            });
        }

        private void btnCancellaPiccoli_Click(object sender, EventArgs e) {
            new Task(() => {
                int n = deleteSmallImages("data");
                Console.WriteLine($"[{ora()}] Cancella piccoli: {n} immagini tolte dalla cache");
            }).Start();
        }

        /// <summary>
        /// Toglie da folder le immagini inutilizzabili e restituisce quante ne ha cancellate:
        /// file sotto i 10.000 byte o illeggibili, segnaposto (altezza sotto 100 px) e immagini
        /// piu' strette della larghezza attuale (min(1000, dimension); 1000 se dimension = 0).
        /// Prima la soglia era 1000 px fissi su larghezza E altezza: con dimension 800 cancellava
        /// tutta la cache e con 1000 le doppie pagine orizzontali; un file corrotto la fermava con
        /// un'eccezione e uno bloccato interrompeva la scansione.
        /// </summary>
        int deleteSmallImages(string folder) {
            if (!Directory.Exists(folder))
                return 0;
            int minWidth = dimension > 0 ? Math.Min(1000, dimension) : 1000;
            int deleted = 0;
            foreach (var fPath in Directory.EnumerateFiles(folder, "*.jpg")) {
                try {
                    bool small;
                    if (new FileInfo(fPath).Length < 10000) {
                        small = true;
                    }
                    else {
                        Size? size = readImageSize(fPath);
                        small = size == null || size.Value.Width < minWidth || size.Value.Height < 100;
                    }
                    if (!small)
                        continue;
                    File.Delete(fPath);
                    forgetSavedFile(Path.GetFileNameWithoutExtension(fPath));
                    deleted++;
                }
                catch (IOException) {
                    // in uso (download o visualizzazione in corso): lo si ritrovera' la prossima volta
                }
                catch (UnauthorizedAccessException) {
                }
            }
            return deleted;
        }

        /// <summary>Dimensioni lette dall'intestazione, senza decodificare l'immagine; null se illeggibile</summary>
        static Size? readImageSize(string path) {
            using (var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                try {
                    using (var img = Image.FromStream(s, false, false))
                        return img.Size;
                }
                catch (ArgumentException) {
                    return null;    // non e' un'immagine
                }
            }
        }

        public static Bitmap EqualizeHistogram(Bitmap inputBitmap) {
            Bitmap grayscaleBitmap = ConvertToGrayscale(inputBitmap);
            int[] histogram = new int[256];
            int width = grayscaleBitmap.Width;
            int height = grayscaleBitmap.Height;

            // Calcolo dell'istogramma
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    Color pixel = grayscaleBitmap.GetPixel(x, y);
                    histogram[pixel.R]++;
                }
            }

            // Calcolo della trasformazione cumulativa
            int totalPixels = width * height;
            int[] cumulativeDistribution = new int[256];
            int cumulativeSum = 0;
            for (int i = 0; i < histogram.Length; i++) {
                cumulativeSum += histogram[i];
                cumulativeDistribution[i] = (cumulativeSum * 255) / totalPixels;
            }

            // Creazione della nuova immagine
            Bitmap equalizedBitmap = new Bitmap(width, height);
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    Color pixel = grayscaleBitmap.GetPixel(x, y);
                    int newIntensity = cumulativeDistribution[pixel.R];
                    if (newIntensity < 0) {
                        newIntensity = 0;
                    }
                    if (newIntensity > 255) {
                        newIntensity = 255;
                    }
                    Color newPixel = Color.FromArgb(newIntensity, newIntensity, newIntensity);
                    equalizedBitmap.SetPixel(x, y, newPixel);
                }
            }

            return equalizedBitmap;
        }

        private static Bitmap ConvertToGrayscale(Bitmap original) {
            Bitmap grayscaleBitmap = new Bitmap(original.Width, original.Height);
            using (Graphics g = Graphics.FromImage(grayscaleBitmap)) {
                ColorMatrix colorMatrix = new ColorMatrix(new float[][]
                {
            new float[] { 0.3f, 0.3f, 0.3f, 0, 0 },
            new float[] { 0.59f, 0.59f, 0.59f, 0, 0 },
            new float[] { 0.11f, 0.11f, 0.11f, 0, 0 },
            new float[] { 0, 0, 0, 1, 0 },
            new float[] { 0, 0, 0, 0, 1 }
                });
                ImageAttributes attributes = new ImageAttributes();
                attributes.SetColorMatrix(colorMatrix);
                g.DrawImage(original, new Rectangle(0, 0, original.Width, original.Height),
                    0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);
            }
            return grayscaleBitmap;
        }
        public static int Clamp(int value, int min, int max) {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }


        public static Bitmap IncreaseContrast(Bitmap inputBitmap, float contrastLevel) {
            Bitmap contrastedBitmap = new Bitmap(inputBitmap.Width, inputBitmap.Height);
            float contrast = (100.0f + contrastLevel) / 100.0f;
            contrast *= contrast;

            for (int y = 0; y < inputBitmap.Height; y++) {
                for (int x = 0; x < inputBitmap.Width; x++) {
                    Color pixel = inputBitmap.GetPixel(x, y);

                    float red = pixel.R / 255.0f;
                    red -= 0.5f;
                    red *= contrast;
                    red += 0.5f;
                    red *= 255;
                    red = Clamp(Convert.ToInt32(red), 0, 255);

                    float green = pixel.G / 255.0f;
                    green -= 0.5f;
                    green *= contrast;
                    green += 0.5f;
                    green *= 255;
                    green = Clamp(Convert.ToInt32(green), 0, 255);

                    float blue = pixel.B / 255.0f;
                    blue -= 0.5f;
                    blue *= contrast;
                    blue += 0.5f;
                    blue *= 255;
                    blue = Clamp(Convert.ToInt32(blue), 0, 255);

                    contrastedBitmap.SetPixel(x, y, Color.FromArgb((int)red, (int)green, (int)blue));
                }
            }

            return contrastedBitmap;
        }

        public static Bitmap ApplyEdgeDetection(Bitmap inputBitmap) {
            Bitmap edgeBitmap = new Bitmap(inputBitmap.Width, inputBitmap.Height);

            // Semplice kernel di Sobel
            int[,] kernelX = { { -1, 0, 1 }, { -2, 0, 2 }, { -1, 0, 1 } };
            int[,] kernelY = { { -1, -2, -1 }, { 0, 0, 0 }, { 1, 2, 1 } };

            for (int y = 1; y < inputBitmap.Height - 1; y++) {
                for (int x = 1; x < inputBitmap.Width - 1; x++) {
                    int gx = 0, gy = 0;

                    for (int ky = -1; ky <= 1; ky++) {
                        for (int kx = -1; kx <= 1; kx++) {
                            Color pixel = inputBitmap.GetPixel(x + kx, y + ky);
                            int intensity = (pixel.R + pixel.G + pixel.B) / 3;
                            gx += intensity * kernelX[ky + 1, kx + 1];
                            gy += intensity * kernelY[ky + 1, kx + 1];
                        }
                    }

                    int edgeStrength = (int)Math.Sqrt(gx * gx + gy * gy);
                    edgeStrength = Clamp(edgeStrength, 0, 255);
                    edgeBitmap.SetPixel(x, y, Color.FromArgb(edgeStrength, edgeStrength, edgeStrength));
                }
            }

            return edgeBitmap;
        }



        private void btnApplicaContrasto_Click(object sender, EventArgs e) {
            var bmp = pic.Image;
            ApplyContrast(contrastBar.Value, (Bitmap)bmp);
            pic.Refresh();
        }

        static byte processImageByte(byte value, int soglia) {
            if (value < soglia) {
                return 0;
            }
            else {
                return 255;
            }
        }

        static unsafe void processImage(Bitmap bmp, int soglia) {
            var bitmapdata = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            int PixelSize = 4;
            for (int y = 0; y < bitmapdata.Height; y++) {
                byte* destPixels = (byte*)bitmapdata.Scan0 + (y * bitmapdata.Stride);
                for (int x = 0; x < bitmapdata.Width; x++) {
                    var val = processImageByte(destPixels[x * PixelSize], soglia);
                    destPixels[x * PixelSize] = val;
                    destPixels[x * PixelSize + 1] = val;
                    destPixels[x * PixelSize + 2] = val;
                    //if (val == 0& x>0) {
                    //    destPixels[x * PixelSize-4]=val;
                    //    destPixels[x * PixelSize-3]=val;
                    //    destPixels[x * PixelSize-2]=val;

                    //}
                }

            }
            bmp.UnlockBits(bitmapdata);
        }

        public unsafe static void ApplyContrast(double contrast, Bitmap bmp) {
            byte[] contrast_lookup = new byte[256];
            double newValue = 0;
            double c = (100.0 + contrast) / 100.0;

            c *= c;

            for (int i = 0; i < 256; i++) {
                newValue = (double)i;
                newValue /= 255.0;
                newValue -= 0.5;
                newValue *= c;
                newValue += 0.5;
                newValue *= 255;

                if (newValue < 0)
                    newValue = 0;
                if (newValue > 255)
                    newValue = 255;
                contrast_lookup[i] = (byte)newValue;
            }

            var bitmapdata = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            int PixelSize = 4;

            for (int y = 0; y < bitmapdata.Height; y++) {
                byte* destPixels = (byte*)bitmapdata.Scan0 + (y * bitmapdata.Stride);
                for (int x = 0; x < bitmapdata.Width; x++) {
                    destPixels[x * PixelSize] = contrast_lookup[destPixels[x * PixelSize]]; // B
                    destPixels[x * PixelSize + 1] = contrast_lookup[destPixels[x * PixelSize + 1]]; // G
                    destPixels[x * PixelSize + 2] = contrast_lookup[destPixels[x * PixelSize + 2]]; // R
                    //destPixels[x * PixelSize + 3] = contrast_lookup[destPixels[x * PixelSize + 3]]; //A
                }
            }

            bmp.UnlockBits(bitmapdata);
        }

		/// <summary>
		/// Carica e visualizza l'immagine n-esima con la dimensione corrente
		/// </summary>
		/// <param name="n"></param>
		async Task viewImage(int n, bool contrastOn, int contrastLevel) {
            if (n == 0) {
                return;
            }
            string fPath = await loadImage(n);
            if (fPath == null) {
                return;
            }
            var bmp = LoadBitmapUnlocked(fPath);
            if (bmp == null) return;
            if (contrastOn)
                processImage(bmp, contrastLevel);
            imageToDisplayNum = n;   // scritto PRIMA
            imageToDisplay = bmp;    // pubblicato per ULTIMO
            //displayImage(imageToDisplay);
            //pic.Refresh();
        }

        /// <summary>
        /// Ricarica la pagina n (col contrasto attuale) e aspetta che sia pronta: la finestra resta in
        /// attesa come prima. Il download gira fuori dal thread della finestra: aspettarlo con
        /// GetAwaiter().GetResult() da qui, con le sue continuation che volevano tornare proprio su
        /// questo thread, bloccava la finestra per sempre quando la pagina non era in cache.
        /// I valori dei controlli si leggono qui, sul thread della finestra.
        /// </summary>
        void viewImageAndWait(int n) {
            bool contrastOn = chkContrast.Checked;
            int contrastLevel = contrastBar.Value;
            Task.Run(() => viewImage(n, contrastOn, contrastLevel)).GetAwaiter().GetResult();
        }

        void reContrast() {
            viewImageAndWait(getNum());
        }
        private void btnLessContrast_Click(object sender, EventArgs e) {
            if (contrastBar.Value > 10)
                contrastBar.Value -= 2;
            reContrast();
        }

        private void btnMoreContrast_Click(object sender, EventArgs e) {
            if (contrastBar.Value < 245)
                contrastBar.Value += 2;
            reContrast();
        }

        private void chkContrast_CheckedChanged(object sender, EventArgs e) {
            reContrast();
        }

        private void contrastBar_ValueChanged(object sender, EventArgs e) {
            txtBar.Text = contrastBar.Value.ToString();
        }



        string expandUrl(string url) {
            using (HttpClient client = new HttpClient()) {
                try {
                    client.Timeout = TimeSpan.FromSeconds(10);

                    // Invia una richiesta HEAD per ottenere la posizione finale
                    HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Head, url);
                    HttpResponseMessage response = client.SendAsync(request).Result;

                    // Ottieni l'URL a cui reindirizza
                    if (response.StatusCode == System.Net.HttpStatusCode.MovedPermanently ||
                        response.StatusCode == System.Net.HttpStatusCode.Found ||
                        response.StatusCode == System.Net.HttpStatusCode.SeeOther ||
                        response.StatusCode == System.Net.HttpStatusCode.TemporaryRedirect) {
                        string expandedUrl = response.Headers.Location.ToString();
                        return expandedUrl;
                    }
                    else {
                        return response.RequestMessage.RequestUri.AbsoluteUri;
                    }
                }
                catch (Exception ex) {
                    Console.WriteLine($"Errore: {ex.Message}");
                }
                return url;
            }

        }




        private bool ServerCertificateValidationCallback(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslpolicyerrors) {
            return true;
        }

        async Task<string> ShortenUrl(string longUrl) {
            var bitly = new Bitly(bitlyCode);
            var linkResponse = await bitly.PostShorten(longUrl);
            return linkResponse.Link;
        }
        void updateVisited() {
            updateHistory();
            if (visited.Count == 0) {
                labVisited.Text = "";
                return;
            }
            labVisited.Text = (currIndex + 1).ToString() + "/" + visited.Count.ToString();
        }
        void pushVisited() {
            var mark = getMark();
            pushHistory(mark);
            if (mark != null) {
                if (visited.Count > 10) {
                    visited.RemoveAt(0);
                    if (currIndex > 0)
                        currIndex--;
                }
                if (mark.inList(visited)) {
                    currIndex = mark.indexIn(visited);
                }
                else {
                    visited.Add(mark);
                    currIndex = visited.Count - 1;
                }

                updateVisited();
            }

        }

        private void contextMenuStrip1_Opening(object sender, CancelEventArgs e) {

        }

        /// <summary>
        /// Ultima pagina vista del registro, riaperta col doppio click (setRegister). Si salva quando la
        /// pagina va davvero a video: prima solo uscendo dal nodo e se la pagina era gia' arrivata,
        /// quindi cambiando registro mentre la pagina caricava non restava traccia.
        /// </summary>
        void rememberPageShown(int page) {
            if (page > 0 && idRegistro != null && Registro.registroById.TryGetValue(idRegistro, out Registro r))
                r.lastPageViewed = page.ToString();
        }

        void GetAppuntiNodo() {
            TreeNode current = treeMain.SelectedNode;
            if (current == null)
                return;
            if (getNodePath(current).Trim() == "")
                return;

            if (txtNodo.Text.Trim() != "") {
                appuntiNodo[getNodePath(current)] = txtNodo.Text;
            }
            else {
                if (appuntiNodo.ContainsKey(getNodePath(current)))
                    appuntiNodo.Remove(getNodePath(current));
            }
            if (idRegistro != null && txtPageNum.Text != "" && txtPageNum.Text == txtEffettivo.Text) {
                if (Registro.registroById.ContainsKey(idRegistro)) {
                    Registro.registroById[idRegistro].lastPageViewed = txtPageNum.Text.Trim();
                }

            }
        }

        void GetTextFromTxt(TextBox t, Dictionary<string, string> dic) {
            TreeNode current = treeMain.SelectedNode;
            if (current == null)
                return;
            string city = getCity(current);
            if (city == null)
                return;
            city = city.Trim();
            if (t.Text.Trim() != "") {
                dic[city] = t.Text;
            }
            else {
                if (dic.ContainsKey(city))
                    dic.Remove(city);
            }
        }
        void GetAppuntiCitta() {
            GetTextFromTxt(txtCitta, appuntiCitta);
        }
        void GetFamiglieCitta() {
            GetTextFromTxt(txtFamiglie, famiglieCitta);
        }

        private void treeMain_BeforeSelect(object sender, TreeViewCancelEventArgs e) {
            GetAppuntiNodo();
            GetAppuntiCitta();
            GetFamiglieCitta();
            //pushVisited();
        }

        private void btnRileggiArchivi_Click(object sender, EventArgs e) {
            Archivio.read(null);
            fillComboArchivi();
        }

        private void btnAddArchive_Click(object sender, EventArgs e) {
            if (cmbArchivio.SelectedIndex < 0)
                return;
            var idArchive = cmbArchivio.SelectedValue as string;
            var a = Archivio.archivioById[idArchive];
            RegNode.addArchiveNodeToTree(a, treeMain.Nodes);
        }

        private void btnEqualize_Click(object sender, EventArgs e) {
            btnEqualize.Visible = false;
            var img = pic.Image;
            img = EqualizeHistogram((Bitmap)img);
            pic.Image = img;
            btnEqualize.Visible = true;
        }

        private void btnGrayScale_Click(object sender, EventArgs e) {
            btnGrayScale.Visible = false;
            var img = pic.Image;
            img = ConvertToGrayscale((Bitmap)img);
            pic.Image = img;
            btnGrayScale.Visible = true;
        }

        private void btnSetContrast_Click(object sender, EventArgs e) {
            btnSetContrast.Visible = false;
            int contrast = toIntOrDefault(txtValore.Text);
            var img = pic.Image;
            img = IncreaseContrast((Bitmap)img, contrast);
            pic.Image = img;
            btnSetContrast.Visible = true;
        }

        private void btnEdge_Click(object sender, EventArgs e) {
            btnEdge.Visible = false;
            var img = pic.Image;
            img = ApplyEdgeDetection((Bitmap)img);
            pic.Image = img;
            btnEdge.Visible = true;
        }

        private void btnReset_Click(object sender, EventArgs e) {
            btnReset.Visible = false;
            viewImageAndWait(currentImage);
            btnReset.Visible = true;
        }

        private void openInBrowserToolStripMenuItem_Click(object sender, EventArgs e) {
            txtWebAddress.Text = getImageUrlByCode(txtCode.Text);
        }

        private void openInBrowserToolStripMenuItem1_Click(object sender, EventArgs e) {
            RegNode rn = treeMain.SelectedNode?.Tag as RegNode;
            if (rn != null) {
                txtWebAddress.Text = rn.archiveNode.href;
                webView.Source = new Uri(txtWebAddress.Text, UriKind.Absolute);
            }
        }

        private void btnGotoRegister_Click(object sender, EventArgs e) {
            var idRegistro = Registro.idFromHref(txtWebAddress.Text);
            if (idRegistro == null) return;
            // come in decodeUrl: registro non ancora nell'albero
            if (!Registro.registroById.ContainsKey(idRegistro)) {
                MessageBox.Show("Manca il registro " + idRegistro + ", lo copio nel codice registro.");
                txtCodiceRegistro.Text = idRegistro;
                return;
            }
            setRegister(idRegistro);
            var nn = RegNode.addArchiveNodeToTree(Registro.registroById[idRegistro], treeMain.Nodes);
            if (nn != null) {
                nn.EnsureVisible();
                treeMain.SelectedNode = nn;
            }
        }

        private void openUrlInBrowserToolStripMenuItem_Click(object sender, EventArgs e) {
            string url = getLongUrl();
            if (url != null) {
                txtWebAddress.Text = url;
                webView.Source = new Uri(url, UriKind.Absolute);
            }
        }
        string getLongUrl() {
            if (txtCode.Text.Trim() == "" || txtCode.Text == "0")
                return null;
            RegNode rn = treeMain.SelectedNode?.Tag as RegNode;
            if (rn != null) {
                return rn.archiveNode.href + "/" + txtCode.Text;
            }
            return null;
        }

        static string bitlyCode = "";
        static string tinyUrlCode = "";
        private async void toolStripMenuItem1_Click(object sender, EventArgs e) {

            string longUrl = getLongUrl();
            if (longUrl == null)
                return;

            var bitly = new Bitly(bitlyCode);
            var linkResponse = await bitly.PostShorten(longUrl);
            var newLink = linkResponse.Link;
            Clipboard.SetText(newLink);
            pushVisited();
            setAsImportant();
        }

        void calcolaAnnoInizio() {
            int anni = toIntOrDefault(txtAnni.Text, 0);

            RegNode rn = treeMain.SelectedNode?.Tag as RegNode;
            if (rn?.archiveNode != null) {
                var an = rn.archiveNode;
                if (an.tipo != tipoNodo.Registro)
                    return;
                var reg = Registro.registroById[an.key];
                var anno = toIntOrDefault(reg.anno) - anni;
                txtAnno.Text = anno.ToString();
            }
        }
        private void txtAnni_TextChanged(object sender, EventArgs e) {
            calcolaAnnoInizio();
        }

        private void btnFindReg_Click(object sender, EventArgs e) {
            gotoRegister(txtCodiceRegistro.Text);
        }

        private void aggiungiAnnoToolStripMenuItem_Click(object sender, EventArgs e) {
            var n = treeMain.SelectedNode?.Tag as RegNode;
            if (n == null)
                return;
            if (n.tipo != tipoNodo.AnnoSerie) {
                MessageBox.Show("Eseguire solo su Anno Serie", "Avviso");
                return;
            }
            var f = new frmAskData("Nuovo Tipo", "Morti/Nati/Diversi/Matrimoni...", "");
            if (f.ShowDialog(this) != DialogResult.OK) {
                return;
            }
            string tipo = f.resultValue;

            AnnoSerie AS = n.archiveNode as AnnoSerie;

            AnnoSerieKind a = new AnnoSerieKind(AS.anno, AS.idSerie, tipo);
            if (AnnoSerieKind.annoSerieKindById.ContainsKey(a.key)) {
                MessageBox.Show("Il tipo già esiste", "Avviso");
                return;
            }
            AnnoSerieKind.annoSerieKindById[a.key] = a;
            AnnoSerieKind.annoSeriesKind.Add(a);
            RegNode.addArchiveNodeToTree(a, treeMain.Nodes);

        }

        private void impostaLocalitàToolStripMenuItem_Click(object sender, EventArgs e) {
            var n = treeMain.SelectedNode?.Tag as RegNode;
            if (n == null)
                return;
            if (n.tipo != tipoNodo.Serie) {
                MessageBox.Show("Eseguire solo su Serie", "Avviso");
                return;
            }
            var f = new frmAskData("Località", "Specificare la località esatta", "");
            if (f.ShowDialog(this) != DialogResult.OK) {
                return;
            }
            string loc = f.resultValue;

            Serie S = n.archiveNode as Serie;
            S.localita = loc;
        }

        private void txtAddress_Click(object sender, EventArgs e) {
            txtWebAddress.Text = txtAddress.Text;
        }

        private void txtAddress2_Click(object sender, EventArgs e) {
            txtWebAddress.Text = txtAddress2.Text;
        }

        RifPage getMark() {
            string rif = txtCodiceRegistro.Text + "#" + txtPageNum.Text;
            string title = getNodePath(treeMain.SelectedNode);

            return new RifPage(rif, title);
        }

        private void btnCopiaRiferimento_Click(object sender, EventArgs e) {
            Clipboard.SetText(getMark().toString());
        }

        private void btnAddToStack_Click(object sender, EventArgs e) {
            pushVisited();
        }

        bool isShortened(string url) {
            bool isHttpShortened = url.StartsWith("http://bit.ly/") || url.StartsWith("http://j.mp/") || url.StartsWith("http://tinyurl.com/");
            bool isHttpsShortened = url.StartsWith("https://bit.ly/") || url.StartsWith("https://j.mp/") || url.StartsWith("https://tinyurl.com/");
            return isHttpShortened || isHttpsShortened;
        }
        string redirectUrl(string url) {
            if (url.StartsWith("http://dl.antenati.san.beniculturali.it"))
                return url;
            if (url.StartsWith("https://antenati.cultura.gov.it/ark:/12657"))
                return url;
            if (isShortened(url)) {
                string expanded = expandUrl(url);
                if (expanded != null)
                    return expanded;
            }
            return url;
        }
        string urlRequested = null;
        void decodeUrl() {
            string url = txtUrlToDecode.Text.Trim();
            int iStart = url.IndexOf("http");
            if (iStart > 0)
                url = url.Substring(iStart);
            url = url.Trim();
            url = url.Split(' ')[0].Trim();  // Se l'indirizzo è seguito da altra roba, ignorala

            url = redirectUrl(url);
            if (url.StartsWith("https://antenati.cultura.gov.it/ark:/12657/")) {
                //https://antenati.cultura.gov.it/ark:/12657/an_ua17439642/wbOG7kv
                string id = Registro.idFromHref(url);
                if (id != null) {
                    idRegistro = id;
                    if (!Registro.registroById.ContainsKey(idRegistro)) {
                        MessageBox.Show("Manca il registro " + idRegistro + ", lo copio nel codice registro.");
                        txtCodiceRegistro.Text = idRegistro;
                        return;
                    }
                    txtUrlToDecode.Text = url;
                    urlRequested = url;
                    toMarkImportant = true;
                    gotoRegister(idRegistro);
                    string pageCode = Registro.pageCodeFromHref(url);
                    if (pageCode != null) {
                        goToCode(pageCode, false);
                    }
                }
            }

            string imgCode = IiifServer.current.codeFromImageUrl(url);
            if (imgCode != null) {
                //Cerca il manifest tra tutti i registri
                foreach (var reg in Registro.registroById.Values) {
                    var idMan = reg.manifestId;
                    if (idMan == null)
                        continue;
                    var man = Manifest.LoadManifest(idMan).Result;
                    if (man.pageDecode.ContainsKey(imgCode)) {
                        string newUrl = reg.href + "/" + imgCode + "/";

                        txtUrlToDecode.Text = newUrl;
                        urlRequested = newUrl;
                        toMarkImportant = true;
                        gotoRegister(reg.idRegistro);
                        goToCode(imgCode, false);
                        return;
                    }
                }

            }

            if (url.IndexOf("#") > 0) {
                gotoRegister(url);
            }
        }
        private void btnDecodeUrl_Click(object sender, EventArgs e) {
            decodeUrl();
            pushHistory(getMark());

        }
        async Task addImportanteImage() {
            var mark = getMark();
            if (mark == null)
                return;
            var fileName = mark.rif + ".jpg";
            string source = await loadImage(getNum());
            if (source == null)
                return;
            try {
                Directory.CreateDirectory("important"); // su un'installazione nuova non c'e' ancora
                System.IO.File.Copy(source, Path.Combine("important", fileName), true);
            }
            catch (Exception e) {
                MessageBox.Show("Errore copiando il file " + Path.Combine("important", fileName));
            }
        }
        void removeImportanteImage() {
            var mark = getMark();
            if (mark == null)
                return;
            var fileName = mark.rif + ".jpg";
            try {
                System.IO.File.Delete(Path.Combine("important", fileName));
            }
            catch (Exception e) {
                MessageBox.Show("Errore eliminando il file " + fileName);
            }
        }
        void setImportante() {
            var curr = getMark();

            if (curr != null) {
                if (important.ContainsKey(curr.rif)) {
                    chkImportante.Checked = true;
                }
                else {
                    chkImportante.Checked = false;
                }
            }
            else {
                chkImportante.Checked = false;
            }
        }
        async void setAsImportant() {
            var curr = getMark();
            string url = getLongUrl();
            if (url == null)
                return;
            if (curr != null) {
                if (!important.ContainsKey(curr.rif)) {
                    important.Add(curr.rif, new Squeeze(await ShortenUrl(url), url));
                    await addImportanteImage();
                    beep(400, 300);
                }
                else {
                    beepSystem();
                }
            }
        }

        private async void chkImportante_CheckedChanged(object sender, EventArgs e) {
            var curr = getMark();
            string url = getLongUrl();
            if (url == null)
                return;
            if (curr != null) {
                if (chkImportante.Checked) {
                    if (!important.ContainsKey(curr.rif)) {
                        important.Add(curr.rif, new Squeeze(await ShortenUrl(url), url));
                        await addImportanteImage();
                    }
                }
                else {
                    if (important.ContainsKey(curr.rif)) {
                        important.Remove(curr.rif);
                        removeImportanteImage();
                    }
                }
            }
        }
        bool toMarkImportant = false;
        private void btnPasteUrl_Click(object sender, EventArgs e) {
            txtUrlToDecode.Text = Clipboard.GetText()?.Trim();

            decodeUrl();
            pushHistory(getMark());
        }

        private void impostaIndiceInternoToolStripMenuItem_Click(object sender, EventArgs e) {
            var reg = getCurrRegister();
            if (reg != null) {
                reg.idRegistroIndice = reg.idRegistro;
                reg.nPaginaIndice = toIntOrDefault(txtEffettivo.Text);
            }
        }
        Registro searchIndexRegister() {
            var n = treeMain.SelectedNode;
            if (n == null)
                return null;
            n = n.Parent;
            if (n == null)
                return null;
            if (n.Text.EndsWith(", indice") || (n.Text.EndsWith(", indici"))) {
                return null;
            }

            //var mainRegName = n.Text.Trim().Replace(", Indice", "");
            var indexRegName = n.Text.Trim() + ", indice";
            var indexRegName2 = n.Text.Trim() + ", indici";
            var par = n.Parent;
            if (par == null)
                return null;
            foreach (TreeNode child in par.Nodes) {
                if (child.Text == indexRegName || child.Text == indexRegName2) {
                    if (child.Nodes.Count == 0) {
                        exploreMainNode(child);
                    }
                    if (child.Nodes.Count > 0) {
                        var reg = child.Nodes[0];
                        if (reg.Tag != null) {
                            RegNode rn = reg.Tag as RegNode;
                            return rn.archiveNode as Registro;
                        }
                    }
                }
            }
            return null;
        }
        Registro searchMainRegister() {
            var n = treeMain.SelectedNode;
            if (n == null)
                return null;
            n = n.Parent;
            if (n == null)
                return null;
            if (!n.Text.EndsWith(", indice") && !n.Text.EndsWith(", indici")) {
                return null;
            }
            var mainRegName = n.Text.Trim().Replace(", indice", "").Replace(", indici", "");
            var par = n.Parent;
            foreach (TreeNode child in par.Nodes) {
                if (child.Text == mainRegName) {
                    if (child.Nodes.Count == 0) {
                        exploreMainNode(child);
                    }
                    if (child.Nodes.Count > 0) {
                        var reg = child.Nodes[0];
                        if (reg.Tag != null) {
                            RegNode rn = reg.Tag as RegNode;
                            return rn.archiveNode as Registro;
                        }
                    }
                    else {
                        tabControl1.SelectedTab = tabPage2;
                        treeMain.SelectedNode = child;
                        treeMain.Focus();
                        return null;
                    }
                }
            }
            return null;
        }

        private void vaiAIndiceToolStripMenuItem_Click(object sender, EventArgs e) {
            toPushHistory = true;
            var reg = getCurrRegister();
            if (reg != null) {
                GetAppuntiNodo();
                if (reg.idRegistroIndice != null && reg.idRegistro != reg.idRegistroIndice) {
                    gotoRegister(reg.idRegistroIndice);
                }
                else {
                    var indexReg = searchIndexRegister();
                    if (indexReg != null) {
                        reg = indexReg;
                        gotoRegister(indexReg.idRegistro);
                    }
                }
                if (reg.nPaginaIndice != 0) {
                    txtPageNum.Text = reg.nPaginaIndice.ToString();
                    txtEffettivo.Focus();
                }
            }
        }

        private void vaiARegistroToolStripMenuItem_Click(object sender, EventArgs e) {
            toPushHistory = true;
            var reg = searchMainRegister();
            if (reg != null) {
                GetAppuntiNodo();
                gotoRegister(reg.idRegistro);
            }
        }

        private void btnFirst_Click(object sender, EventArgs e) {
            toPushHistory = true;
            txtPageNum.Text = "1";
        }

        bool toPushHistory = false;
        private void btnLast_Click(object sender, EventArgs e) {
            toPushHistory = true;
            txtPageNum.Text = txtTotPages.Text;
        }


        void addDeltaAnnoToCurrent(int delta) {

            string path = getNodePath(treeMain.SelectedNode);
            path = path.TrimEnd('/');
            var parts = path.Split(new char[] { '/' });
            if (parts.Length < 3)
                return;
            int anno = toIntOrDefault(parts[parts.Length - 3]);
            bool moved = true;
            if (anno > 0) {
                anno = anno + delta;
                parts[parts.Length - 3] = anno.ToString();
                if (!setNodePath(treeMain, String.Join("/", parts.ToList()))) {
                    beepSystem();
                    if (parts[parts.Length - 2].Contains(", indice") || parts[parts.Length - 2].Contains(", indici")) {
                        parts[parts.Length - 2] = parts[parts.Length - 2].Replace(", indice", "").Replace(", indici", "");
                        if (!setNodePath(treeMain, String.Join("/", parts.ToList()))) {  // va al registro se possibile
                            //Se non è possibile torna alla posizione di partenza
                            setNodePath(treeMain, path);
                            beep(400, 500);
                            moved = false;
                        }
                    };
                }

                toPushHistory = moved;
            }
        }
        private void btnAnnoPrima_Click(object sender, EventArgs e) {
            addDeltaAnnoToCurrent(-1);
        }

        private void btnAnnoDopo_Click(object sender, EventArgs e) {
            addDeltaAnnoToCurrent(1);
        }

        private void btLyCode_Click(object sender, EventArgs e) {
            var f = new frmAskData("Impostazione Bitly", "codice API license", bitlyCode);
            if (f.ShowDialog(this) == DialogResult.OK) {
                bitlyCode = f.resultValue;
            }
        }

        private void toolStripMenuItem2_Click(object sender, EventArgs e) {
            Clipboard.SetText(getMark().toString());
        }

        // Archivio genealogico GEDCOM (ricerca e schede): vedi GedcomSearch
        GedcomSearch genealogy = null;

        bool readGedFile(string fileName) {
            var g = GedcomSearch.Load(fileName);
            if (g == null) {
                MessageBox.Show("Errore", "File Gedcom non valido");
                return false;
            }
            genealogy = g;
            return true;
        }
        private void btnLeggiGedCom_Click(object sender, EventArgs e) {
            FileDialog f = new OpenFileDialog();
            f.Title = "Select Ged file";
            f.DefaultExt = "ged";
            f.CheckFileExists = true;
            f.CheckPathExists = true;
            var res = f.ShowDialog(this);
            if (res != DialogResult.OK) {
                return;

            }
            readGedFile(f.FileName);
        }

        /// <summary>Opzioni di presentazione lette dalle caselle della scheda di ricerca</summary>
        DescriptionOptions descriptionOptions() {
            return new DescriptionOptions {
                FullNames = chkFullNames.Checked,
                FullDates = chkFullDate.Checked,
                ShowSpouse = chkShowSpouse.Checked,
                ShowKey = chkKeys.Checked,
                ShowAllKeys = chkFullKey.Checked,
            };
        }

        void searchNames() {
            if (genealogy == null) { return; }
            StringBuilder sb = new StringBuilder();
            if (txtSearchName.Text == "")
                return;

            foreach (var line in genealogy.Search(txtSearchName.Text, descriptionOptions())) {
                sb.AppendLine(line);
            }
            txtSearchResult.Text = sb.ToString();
        }
        private void txtSearchName_Leave(object sender, EventArgs e) {
            searchNames();
        }

        private void chkFullDate_CheckedChanged(object sender, EventArgs e) {
            searchNames();
        }

        private void chkFullNames_CheckedChanged(object sender, EventArgs e) {
            searchNames();
        }

        private void chkShowSpouse_CheckedChanged(object sender, EventArgs e) {
            searchNames();
        }

        private void btnScreen_Click(object sender, EventArgs e) {
            var r = getCurrRegister();
            if (r == null)
                return;
            string code = r.idRegistro;
            if (r.manifest == null)
                return;
            StringBuilder sb = new StringBuilder();
            foreach (var p in r.manifest.mapPage.Keys) {
                string mark = code + "#" + p;
                if (important.ContainsKey(mark)) {
                    sb.AppendLine(mark);
                }
            }
            txtScreen.Text = sb.ToString();
        }

        private void historyList_DoubleClick(object sender, EventArgs e) {
            var rif = historyList.SelectedItem as string;
            if (String.IsNullOrEmpty(rif))
                return;
            rif = rif.Split(new string[] { " - " }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
            beep(100, 100);
            Clipboard.SetText(rif);
            gotoRegister(rif);
            pushHistory(getMark());
        }

        private void btnRotateReg_Click(object sender, EventArgs e) {
            var sel = treeMain.SelectedNode;
            if (sel == null) {
                return;
            }
            var par = sel.Parent;
            if (par == null) {
                return;
            }
            TreeNode newNode = null;
            for (int i = 0; i < par.Nodes.Count; i++) {
                var node = par.Nodes[i];
                if (!node.IsSelected)
                    continue;
                if (i < par.Nodes.Count - 1) {
                    newNode = par.Nodes[i + 1];
                }
                else {
                    newNode = par.Nodes[0];
                }
            }
            if (newNode == null)
                return;
            newNode.EnsureVisible();
            treeMain.SelectedNode = newNode;
            treeMain.Focus();
        }

        private void btnAppendRif_Click(object sender, EventArgs e) {
            string s = txtObiettivi.Text;
            s += "\r\n" + getMark().toString() + "\r\n";
            txtObiettivi.Text = s;
        }

        private void btnPiuUno_MouseEnter(object sender, EventArgs e) {
            mouseOverBtnPiuUno = true;
        }

        private void btnMenoUno_MouseEnter(object sender, EventArgs e) {
            mouseOverBtnMenoUno = true;
        }

        private void btnMenoUno_MouseLeave(object sender, EventArgs e) {
            mouseOverBtnMenoUno = false;
        }

        private void btnPiuUno_MouseLeave(object sender, EventArgs e) {
            mouseOverBtnPiuUno = false;
        }

        private void btnUpsideDown_Click(object sender, EventArgs e) {
            if (pic.Image == null)
                return;
            var i = pic.Image;
            i.RotateFlip(RotateFlipType.Rotate180FlipNone);
            pic.Image = i;
        }

        private void btnRotLeft_Click(object sender, EventArgs e) {
            if (pic.Image == null)
                return;
            var i = pic.Image;
            i.RotateFlip(RotateFlipType.Rotate270FlipNone);
            pic.Image = i;
        }

        private void btRotRight_Click(object sender, EventArgs e) {
            if (pic.Image == null)
                return;
            var i = pic.Image;
            i.RotateFlip(RotateFlipType.Rotate90FlipNone);
            pic.Image = i;
        }
        void longSearch() {
            // l'identificativo si accetta con o senza "XREF" (lo gestisce GedcomSearch)
            string descr = genealogy?.Describe(txtID.Text, descriptionOptions());
            if (descr != null)
                txtLongFam.Text = descr;
        }
        private void txtID_Leave(object sender, EventArgs e) {
            longSearch();
        }

 


        private void chkKeys_CheckedChanged(object sender, EventArgs e) {
            searchNames();
        }

        private void fullKey_CheckedChanged(object sender, EventArgs e) {
            longSearch();
        }

        int dimension = 0;
        /// <summary>
        /// La dimensione scritta in txtDimensione si applica uscendo dal campo o con Invio, non a ogni
        /// tasto: con TextChanged, scrivendo "2000" la cache veniva svuotata 4 volte e nel frattempo si
        /// chiedevano al server immagini larghe 2, 20 e 200 px. Chiamato dal costruttore.
        /// </summary>
        void wireDimensionBox() {
            txtDimensione.Leave += (s, e) => applyDimension();
            txtDimensione.KeyDown += (s, e) => {
                if (e.KeyCode == Keys.Enter) {
                    applyDimension();
                    e.SuppressKeyPress = true;
                }
            };
        }

        void applyDimension() {
            int oldDimension = dimension;
            int dim = toIntOrDefault(txtDimensione.Text?.Trim() ?? "", 0);
            dimension = dim > 0 ? dim : 0;   // vuoto o non valido = dimensione nativa (/full/max/)
            // Le immagini in cache sono salvate per codice, senza larghezza: vanno buttate solo se la
            // larghezza cambia davvero.
            if (dimension != oldDimension)
                deleteCache();
        }
    }





}
