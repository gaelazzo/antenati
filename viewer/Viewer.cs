using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Net.Http;
using BitlyAPI;
using System.Text.RegularExpressions;
using System.Globalization;
using Newtonsoft.Json;
using Control = System.Windows.Forms.Control;
using GedcomParser;
using Gedcom;
using System.Net.Http.Headers;
using System.Text.Json;



namespace viewer {




    public partial class Viewer : Form {
        static object hashSemaphore = new object();
        HashSet<string> existingRequests = new HashSet<string>();

        /// <summary>
        /// Gets the path for the image of a specified code in the current register
        /// </summary>
        /// <param name="code"></param>
        /// <returns></returns>
        string PathFromCode(string code) {
            if (getCurrRegister() == null)
                return null;
            string fName = code + ".jpg";
            fName = Regex.Replace(fName, "([A-Z])", "_$1");
            string fPath = Path.Combine("data", getCurrRegister().idRegistro, fName);
            return fPath;
        }

        void addRequest(string code) {
            lock (hashSemaphore) {
                existingRequests.Add(PathFromCode(code));
            }
        }
        string getImageUrlByCode(string code) {
            int width = mapWidth[code];
			// {scheme}://{server}/iiif/2/{identifier}/{region}/{size}/{rotation}/{quality}.{format}
			//return "https://iiif-antenati.cultura.gov.it/iiif/2/" + code + "/full/full/0/default.jpg";
			// https://iiif-antenati.cultura.gov.it/iiif/2/5VrBEzV/0,2048,1024,518/1024,/0/default.jpg

			return $"https://iiif-antenati.cultura.gov.it/iiif/2/{code}/full/{width},/0/default.jpg";

		}

		void removeRequest(string code) {
            lock (hashSemaphore) {
                if (existingRequests.Contains(PathFromCode(code)))
                    existingRequests.Remove(PathFromCode(code));
            }
        }

        bool checkFileRequestExists(string code) {
            lock (hashSemaphore) {
                if (existingRequests.Contains(PathFromCode(code)))
                    return true;
            }

            if (System.IO.File.Exists(PathFromCode(code))) {
                lock (hashSemaphore) {
                    existingRequests.Add(PathFromCode(code));
                    return true;
                }
            }

            return false;
        }

        class noteToAdd {
            public System.Windows.Forms.TreeNodeCollection collection;
            public System.Windows.Forms.TreeNode n;
            public string filename;
            public bool autoexplore;
            public bool toGo = false;
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
			PageLoader.getResponseMessage("https://antenati.cultura.gov.it/");
            PageLoader.getResponseMessage("https://analytics-icar.cultura.gov.it/matomo.js");
			PageLoader.getResponseMessage("https://www.googletagmanager.com/gtag/js?id=G-HPLTCJ58MW");
			PageLoader.getResponseMessage("https://antenati.cultura.gov.it/ark:/12657/an_ua244110/LPbBpOd/");
			


			statusLabel = new ToolStripStatusLabel("Pronto");
            statusStrip.Items.Add(statusLabel);

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
            ServicePointManager.ServerCertificateValidationCallback += ServerCertificateValidationCallback;
            if (!Directory.Exists("data"))
                Directory.CreateDirectory("data");
            fillComboArchivi();


            var files = Directory.EnumerateFiles("data", "*.*", SearchOption.AllDirectories);
            foreach (var f in files) {
                savedFiles.Add(Path.Combine(Path.GetFileNameWithoutExtension(f)));
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

            //btnCalcolaCitta_Click(null, null);
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
        private Dictionary<string, int> mapWidth = new Dictionary<string, int>();
		private Dictionary<string, int> pageDecode = new Dictionary<string, int>();

        void clearMaps() {
            mapPage.Clear();
            pageDecode.Clear();
            mapWidth.Clear();
		}



        //https://antenati.cultura.gov.it/ark:/[appCode]/an_ua[registro]/[codepage]


        Manifest currManifest = null;
        //Manifest indexManifest = null;





        string idRegistro = null;
        //string archivio = "";
        //string registerName = "";
        //string Comune = "";
        //string Anno = "";
        //string tipologia = "";


        string pageCode = "";
        int pageNumber = 0;

        /// <summary>
        /// Legge i dati e la codifica da un url
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        public async Task<string> loadPage(string url) {
            string manifestId = ManifestIdExtractor.ExtractManifestId(url);

            var manifest = await Manifest.LoadManifest(manifestId);
            var uri = new Uri(url);
            var segments = uri.Segments;
            if (segments.Length > 4) { // Il segmento aggiuntivo è presente
                string lastSegment = segments[segments.Length - 1]; // Ottieni l'ultimo segmento
                return setPageByCode(lastSegment);
            }
            else { // L'URL termina senza il segmento aggiuntivo
                return setPageByNumber(1);
            }
        }


        string setPageByCode(string code) {
            if (pageDecode.ContainsKey(pageCode)) {
                pageCode = code;
                pageNumber = pageDecode[pageCode];
                return loadJpg(pageCode);
            }
            return pageCode;
        }

        string setPageByNumber(int nPage) {

            if (mapPage.ContainsKey(nPage)) {
                pageNumber = nPage;
                pageCode = mapPage[pageNumber];
                return loadJpg(pageCode);
            }
            return null;
        }

        string loadJpg(string code) {
			var imgUrl = getImageUrlByCode(code);
			return loadImage(imgUrl);

        }

        /// <summary>
        /// Azzera la current image facendo in modo che il prossimo da visitare sia quello nel txt
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnGo_Click(object sender, EventArgs e) {
            if (mapPage == null)
                return;
            int n = getNum();
            if (!mapPage.ContainsKey(n))
                return;
            string code = mapPage[n];
            if (n != 0) {
                removeRequest(PathFromCode(code));
            }

            currentImage = 0;
        }

        private int currPrecaching = 0;

        void precache(int start, int num, bool forward) {
            if (mapPage == null)
                return;

            lock (semaforo) {
                if (isCaching)
                    return;
                isCaching = true;
            }

            int curr = start;
            int step = forward ? calcStep() : -calcStep();

            for (int i = 0; i < num; i++) {
                if (stopCaching) {
                    stopCaching = false;
                    break;
                }
                if (!mapPage.ContainsKey(curr)) {
                    break;
                }

                /*new Task((o_currPrecaching) => {
                    int currPrecaching = (int)o_currPrecaching;
                    if (loadImage(mapPage[currPrecaching]) == null) {
                        stopCaching = true;
                    }
                        
                },curr).Start();
                */
                currPrecaching = curr;
                if (loadImage(mapPage[curr]) == null)
                    break;
                curr += step;

            }

            currPrecaching = 0;
            lock (semaforo) {
                isCaching = false;
            }
        }

        private int cacheSize = 10;

        void startPrecache(int start, bool forward) {
            if (isCaching)
                return;
            cacheSize = 10;
            if (txtCacheSize.Text != "") {
                int.TryParse(txtCacheSize.Text, out cacheSize);
            }

            new Task(() => { precache(start, cacheSize, forward); }).Start();
        }

        private bool isCaching = false;




        string loadImage(int num) {
            if (!mapPage.ContainsKey(num)) {
                return null;
            }
            return loadImage(mapPage[num]);
        }

        string loadImage(string code) {
            if (checkError(code))
                return null;
            if (!pageDecode.ContainsKey(code))
                return null;
            int num = pageDecode[code];
            string codePrec = mapPage[num];
            string fPath = PathFromCode(code);
            if (savedFiles.Contains(fPath))
                return fPath;
            try {
                if (!checkFileRequestExists(code)) {
                    addRequest(code);
                    if (!queueDownloadFile(code, fPath, false)) {
                        removeRequest(code);
                        return null;
                    }

                    var f = new FileInfo(fPath);
                    if (f.Length < 10000) {
                        System.IO.File.Delete(fPath);
                        removeRequest(code);
                        return null;
                    }

                    var imageToDisplay = System.Drawing.Image.FromFile(fPath);
                    if (imageToDisplay.Height < 1000 || imageToDisplay.Width < 1000) {
                        removeRequest(code);
                        return null;
                    }
                    if (num > 0) {
                        string fNamePrec = codePrec + ".jpg";
                        string fPathPrec = PathFromCode(codePrec);
                        if (System.IO.File.Exists(fPathPrec)) {
                            var fPrec = new FileInfo(fPathPrec);
                            if (fPrec.Length < f.Length) {
                                if (savedFiles.Contains(codePrec.ToString()))
                                    savedFiles.Remove(codePrec.ToString());
                                System.IO.File.Delete(fPathPrec);
                            }
                        }
                    }


                    esistenti.Add(PathFromCode(code));

                    imageToDisplay.Dispose();

                    savedFiles.Add(PathFromCode(code));

                    removeRequest(code);
                }
                return fPath;
            }
            catch (Exception e) {
                MessageBox.Show(e.ToString(), "Errore");
                if (System.IO.File.Exists(fPath)) {
                    try {
                        System.IO.File.Delete(fPath);
                    }
                    catch {

                    }
                }

                removeRequest(code);
                return null;
            }
        }

        int xScroll = 0;
        int yScroll = 0;
        int zoom = 0;
        void displayImage(Image imgBase) {
            if (drawing)
                return;
            drawing = true;


            bool thereWasImage = (lastImage != null);
            if (imgBase != null && lastImage != imgBase) {
                lastImage?.Dispose();
                lastImage = imgBase;
            }
            else {
                imgBase = lastImage;
            }

            if (imgBase == null) {
                drawing = false;
                return;
            }

            try {
                trackZoom.Value = zoom;
                var zoomFactor = Convert.ToDouble(zoom) / 1000.0;
                Size newSize = new Size((int)(Math.Round(Convert.ToDouble(imgBase.Width * zoomFactor))),
                    (int)(Math.Round(Convert.ToDouble(imgBase.Height * zoomFactor))));
                var img = new Bitmap(imgBase, newSize);

                //imgBase.Dispose();
                if (chkContrast.Checked) {
                    processImage((Bitmap)img, contrastBar.Value);
                    //ApplyContrast(contrastBar.Value,(Bitmap) imgBase);
                }

                var lastxRate = pic.Image == null ? 0 : xScroll * 1.0 / pic.Image.Width;
                var lastyRate = pic.Image == null ? 0 : yScroll * 1.0 / pic.Image.Height;
                //pic.Image?.Dispose();

                pic.Image = img;
                if (thereWasImage) {
                    var val = Convert.ToInt32(Math.Round(lastxRate * img.Width));
                    if (val < imgPanel.HorizontalScroll.Minimum)
                        val = imgPanel.HorizontalScroll.Minimum;
                    if (val > imgPanel.HorizontalScroll.Maximum)
                        val = imgPanel.HorizontalScroll.Maximum;
                    imgPanel.HorizontalScroll.Value = val;
                    xScroll = val;

                    val = Convert.ToInt32(Math.Round(lastyRate * img.Height));
                    if (val < imgPanel.VerticalScroll.Minimum)
                        val = imgPanel.VerticalScroll.Minimum;
                    if (val > imgPanel.VerticalScroll.Maximum)
                        val = imgPanel.VerticalScroll.Maximum;

                    imgPanel.VerticalScroll.Value = val;
                    yScroll = val;
                }
            }
            catch (Exception e) {
                string err = e.ToString();
            }

            drawing = false;
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
            mapWidth = new Dictionary<string, int>();
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


            Registro r = Registro.registroById[idRegister];
            if (r.nPaginaIndice != 0 && r.idRegistroIndice == null) {
                r.idRegistroIndice = r.idRegistro;
            }

            if (r == null) {
                clearRegister();
                setTotPages();
                return;
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
            mapWidth = currManifest.pageWidth;

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
            lbCity.Text = getCity(treeMain.SelectedNode) + " " + r.kind ?? "";
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
        Semaphore Sdownload = new Semaphore(3, 3);
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
            stopCaching = true;
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
                Console.Beep(2000, 200);
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



            if (webView.Source?.AbsoluteUri != null)
                txtCurrent.Text = webView.Source.AbsoluteUri;

            updateCreaIndiceVisible();

            btnStop.Visible = isCaching && !stopCaching;
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

            if (requestedImage != nEffettivo && updating == false && requestedImage != 0) {
                updating = true;
                currentImage = requestedImage; //nuova immagine richiesta
                string code = null;
                if (mapPage.ContainsKey(requestedImage)) {
                    code = mapPage[requestedImage];
                    //txtWebAddress.Text = getImageUrlByCode(code);
                }

                //tabControl1.SelectedTab = tabPage1;
                new Task(() => {
                    string fPath;
                    try {
                        if (mapPage.ContainsKey(requestedImage)) {
                            int saved = requestedImage;
                            startPrecache(currentImage + standardStep, true);
                            fPath = loadImage(mapPage[saved]);
                            if (fPath == null) {
                                updating = false;
                                toMarkImportant = false;
                                return;
                            }

                            try {
                                if (saved == currentImage) {
                                    imageToDisplay = Image.FromFile(fPath);
                                    nEffettivo = saved;
                                }
                            }
                            catch (FileNotFoundException f) {
                                //savedFiles.Remove(fPath);                               
                            }
                            catch {
                            }

                        }

                    }
                    catch {
                    }

                    updating = false;

                }).Start();
            }

            if (imageToDisplay != null) {
                displayImage(imageToDisplay);
                if (toPushHistory) {
                    toPushHistory = false;
                    pushHistory(getMark());
                }
                imageToDisplay = null;
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
            inTimer = false;

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
        HashSet<string> esistenti = new HashSet<string>();

        bool checkError(string code, int step = 0) {
            if (pageDecode == null)
                return true;
            if (!pageDecode.ContainsKey(code))
                return true;
            if (step == 0) {
                return false;
            }
            var pageIndex = pageDecode[code];
            if (!mapPage.ContainsKey(pageIndex + step)) {
                return true;
            }
            return false;
        }

        private bool updating = false;

        private int nEffettivo = 0;


        private Image imageToDisplay = null;

        private int lastLen = 0;

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

            zoom = trackZoom.Value;
            displayImage(null);
        }

        void zoomStepIn() {
            try {
                if (trackZoom.Value + 50 > trackZoom.Minimum)
                    trackZoom.Value += 50;
            }
            catch {

            }
        }
        void zoomStepOut() {
            try {
                if (trackZoom.Value - 50 > trackZoom.Minimum)
                    trackZoom.Value -= 50;
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
            System.IO.File.Copy(loadImage(getNum()), saveFileDialog1.FileName, true);
        }

        HashSet<string> savedFiles = new HashSet<string>();

        private bool stopCaching = false;

        private void btnStop_Click(object sender, EventArgs e) {
            stopCaching = true;
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



        //public void queueDownloadFile(string url, string filename) {
        //    if (checkFileRequestExists(filename)) return;
        //    WebClient w = null;
        //    try {
        //        addRequest(filename);
        //        Sdownload.WaitOne();
        //        w = new WebClient();
        //        w.DownloadFile(url, filename);
        //    }
        //    catch {
        //    }
        //    finally {
        //        Sdownload.Release();
        //        w?.Dispose();
        //        removeRequest(filename);
        //    }

        //}


        public bool queueDownloadFile(string code, string path, bool forced = true) {
            WebClient w = null;
            string folder = Path.GetDirectoryName(path);
            if (!Directory.Exists(folder)) {
                Directory.CreateDirectory(folder);
            }


            string fileName = Path.GetTempPath() + Guid.NewGuid().ToString() + ".jpg";
            try {

                if (!forced)
                    Sdownload.WaitOne();


                string url =getImageUrlByCode(code);  //  "https://iiif-antenati.cultura.gov.it/iiif/2/LPbBpOd/full/956,/0/default.jpg"
				currExploring = url;

				Dictionary<string, string> cookies = new Dictionary<string, string>();
    //            var _ga = PageLoader.getCookie("_ga");  //GA1.1.1208148208.1762619312
				//var _gid = PageLoader.getCookie("_ga_HPLTCJ58MW");

    //            if (_ga == null) {
    //                return false;
    //            }
				//Console.WriteLine("Reading address " + url);
				//cookies.Add("_ga", _ga);
				//cookies.Add("_ga_HPLTCJ58MW", _gid);  //GS2.1.s1762619311$o1$g1$t1762622216$j58$l0$h0
				cookies.Add("_ga", "GA1.1.1208148208.1762619312");
				cookies.Add("_ga_HPLTCJ58MW", "GS2.1.s1762676767$o4$g1$t1762676853$j59$l0$h0");  //GS2.1.s1762619311$o1$g1$t1762622216$j58$l0$h0


				Dictionary<string, string> headers = new Dictionary<string, string>();
				//headers.Add("Accept", "*/*");
				//headers.Add("Accept-Encoding", "gzip, deflate, br, zstd");
				//headers.Add("Accept-Language", "it,en-US;q=0.9,en;q=0.8");
				//headers.Add("Origin", "https://antenati.cultura.gov.it");
				//headers.Add("Priority", "u=1, i");
				headers.Add("Referer", "https://antenati.cultura.gov.it");

				headers.Add("Sec-Ch-Ua", @"""Chromium"";v=""142"", ""Google Chrome"";v=""142"", ""Not?A_Brand"";v=""99""");
				headers.Add("Sec-Ch-Ua-Mobile", "?0");
				headers.Add("Sec-Ch-Ua-Platform", "\"Windows\"");
				headers.Add("Sec-Fetch-Dest", "empty");
				headers.Add("Sec-Fetch-Mode", "cors");
				headers.Add("Sec-Fetch-Site", "same-site");
				headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36");


				

				var response = PageLoader.getResponseMessage(url, HttpCompletionOption.ResponseContentRead, headers, cookies);

				if (response == null || !response.IsSuccessStatusCode) {
					Console.WriteLine($"HTTP Error {(int?)response?.StatusCode}: {response?.ReasonPhrase}");
					return false;
				}
				using (var fs = new FileStream(fileName, FileMode.Create, FileAccess.Write)) {
					using (var stream = response.Content.ReadAsStreamAsync().Result) {
						stream.CopyTo(fs);
					}
				}

				
                var f = new FileInfo(fileName);
                if (f.Length > 10000) {
                    System.IO.File.Copy(fileName, path);
                }
                System.IO.File.Delete(fileName);
                if (currExploring == url)
                    currExploring = "";
                return true;
            }
            catch (Exception e) {
                string eee = e.ToString();
                
				Console.WriteLine("queueDownloadFile:"+e.Message);
			}
            finally {
                if (!forced)
                    Sdownload.Release();
                w?.Dispose();
            }

            return false;
        }

        /// <summary>
        /// Scarica la risorsa indicata da un url
        /// </summary>
        /// <param name="url"></param>
        /// <param name="forced">Se forced è true ammette il download bypassando i semafori</param>
        /// <returns></returns>
        public string queueDownloadString(string url, bool forced = true) {
            string s = null;
            WebClient w = null;
            try {
                if (!forced)
                    Sdownload.WaitOne();
                w = new WebClient();
                currExploring = url;
                s = w.DownloadString(url);
            }
            catch {
            }
            finally {
                if (!forced)
                    Sdownload.Release();
                w?.Dispose();
            }

            return s;
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
            if (!String.IsNullOrEmpty(gedFileName)) {
                s.AppendLine("gedFileName§" + gedFileName);
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


        void readStartValues() {
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
                        txtPageNum.Text = pieces[1];
                        viewImage(Convert.ToInt32(pieces[1]));
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
                        zoom = Convert.ToInt32(pieces[1]);
                        trackZoom.Value = zoom;
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
        void voidStartValues() {
            if (System.IO.File.Exists("start.txt")) {
                var n = System.IO.File.ReadAllText("start.txt");
                StringReader sr = new StringReader(n);
                while (sr.Peek() > -1) {
                    string line = sr.ReadLine().Trim();
                    if (string.IsNullOrEmpty(line))
                        continue;
                    string[] pieces = line.Split(';');
                    esistenti.Add(pieces[1]);
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
            File.WriteAllText("history.txt", s.ToString());
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
            System.IO.File.WriteAllText(fileName, s.ToString());
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

            System.IO.File.WriteAllText("indice.txt", s.ToString());
            System.IO.File.WriteAllText("note.txt", txtGenerali.Text);
            System.IO.File.WriteAllText("note2.txt", txtNotes2.Text);
            System.IO.File.WriteAllText("obiettivi.txt", txtObiettivi.Text);
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




        Dictionary<string, bool> previouslyAdded = new Dictionary<string, bool>();



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
                    Console.Beep(200, 50);
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
                            if (treeViewCitta.SelectedNode != foundCitta)
                                treeViewCitta.SelectedNode = foundCitta;
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



        string getAddress() {
            return txtAddress.Text.Trim();
        }





        void exploreMainNode(TreeNode nn) {

            var regN = nn.Tag as RegNode;
            pushStatus($"Explore {regN.key} {regN.title}");
            labExplore.Text = regN.archiveNode.href;
            labExplore.Update();
            var nodes = regN.archiveNode.explore() ?? new List<IArchiveNode>();
            labExplore.Text = currExploring;
            popStatus();
            if (regN.archiveNode.tipo == tipoNodo.Registro) {
                setRegister(regN.key);
            }
            foreach (var n in nodes) {
                RegNode.addArchiveNodeToTree(n, treeMain.Nodes);
            }
            nn.Expand();
        }

        private void treeMain_DoubleClick(object sender, EventArgs e) {
            if (treeMain.SelectedNode == null) {
                return;
            }
            exploreMainNode(treeMain.SelectedNode);


            /*
            //Esplora solo se il nodo non è già associato ad un oggetto
            waitingToGo = getAddress();
            addAddress(waitingToGo, false);
            exploreMainNode(treeMain.SelectedNode);
            
            RegNode rn = treeMain.SelectedNode.Tag as RegNode;
            if (rn.tipo == tipoNodo.Registro) {
                var r = Registro.registroById[rn.key];
                setRegister(rn.key);
            }
            txtPageNum.Text = "1";
            int num = 0;
            if (int.TryParse(txtPageNum.Text, out num)) {
                addToList(num);
            }
            
            currentImage = 0;
            */
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
            if (registro != null & registro.idRegistroIndice != null) {
                //Codice per impostare mappa idRegistroIndice
                //TODO  
                int N = registro.nPaginaIndice;
                setRegister(registro.idRegistroIndice);
                goToNumber(N);
            }

        }


        /*
        private void btnIntegraIndice_Click(object sender, EventArgs e) {
            btnIntegraIndice.Visible = false;
            Dictionary<string, int> unindexed = new Dictionary<string, int>();
            StringBuilder s = new StringBuilder();
            saveNodes(s);
            StringReader sr = new StringReader(s.ToString());

            while (sr.Peek() > -1) {
                string line = sr.ReadLine().Trim();
                if (string.IsNullOrEmpty(line)) return;

                List<string> parts = line.Split('/').ToList();
                if (parts.Count < 4) continue;
                int indexValue;
                if (int.TryParse(parts.Last(), out indexValue)) {
                    parts.RemoveAt(parts.Count - 1);
                    string unindexedKey;
                    //if (unindexedKey.EndsWith("/")) unindexedKey.Remove(unindexedKey.Length - 1);
                    if (parts.Count < 4) continue;
                    if (parts[3].EndsWith("+indice")) {
                        parts[3] = parts[3].Substring(0, parts[3].Length - 7);
                        unindexedKey = string.Join("/", parts);
                        if (unindexed.ContainsKey(unindexedKey)) {
                            if (!index.ContainsKey(unindexed[unindexedKey])) {
                                index[unindexed[unindexedKey]] = indexValue;
                            }

                        }
                    }
                    else {
                        if (parts.Count < 4) continue;
                        if (parts.Count > 5) parts.RemoveAt(parts.Count - 1);
                        unindexedKey = string.Join("/", parts);
                        unindexed[unindexedKey] = indexValue;
                    }
                }
            }

            btnIntegraIndice.Visible = true;

        }
        */

        string getIdRegister(TreeNode node) {
            var rn = node?.Tag as RegNode;
            if (rn == null)
                return null;
            if (rn.tipo != tipoNodo.Registro)
                return null;
            return rn.key;
        }

        private void treeViewCitta_AfterSelect(object sender, TreeViewEventArgs e) {
            bool indexAvailable = false;
            if (treeViewCitta.SelectedNode != null) {
                var currNode = treeViewCitta.SelectedNode;
                if (currNode.Tag != null) {
                    var currReg = currNode.Tag as RegNode;
                    var reg = currReg.archiveNode as Registro;

                    TreeNode foundMain = searchByRegistro(reg.idRegistro, treeMain.Nodes);
                    if (foundMain != null) {
                        foundMain.EnsureVisible();
                        if (treeMain.SelectedNode != foundMain) {
                            treeMain.SelectedNode = foundMain;
                        }

                    }
                }
            }

        }
        public int toIntOrDefault(string input, int valore = 1) {
            return int.TryParse(input, out int result) ? result : valore;
        }

        private void btnViewIndex2_Click(object sender, EventArgs e) {
            pushHistory(getMark());
            var indexReg = getCurrIndexRegister();
            if (indexReg != null) {
                var nPagina = indexReg.nPaginaIndice;
                goToNumber(nPagina);
            }
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

        private void impostaPaginaToolStripMenuItem_Click(object sender, EventArgs e) {
            var n = treeMain.SelectedNode;
            if (n == null)
                return;

            var f = new frmAskData("Pagina associata al nodo", "Numero pagina", n.Tag?.ToString() ?? "");
            if (f.ShowDialog(this) == DialogResult.OK) {
                n.Tag = f.resultValue;
            }
        }

        private void impostaIndiceToolStripMenuItem_Click(object sender, EventArgs e) {

            var reg = getCurrRegister();
            var indexReg = getCurrIndexRegister();

            var f = new frmAskData("N. pagina indice associato al nodo", "N. Pagina indice",
                    indexReg == null ? "" : indexReg.nPaginaIndice.ToString());
            if (f.ShowDialog(this) == DialogResult.OK) {
                int N = toIntOrDefault(f.resultValue);
                if (N != 0) {
                    reg.idRegistroIndice = null;
                    reg.nPaginaIndice = 0;
                    return;
                }
                reg.nPaginaIndice = N;
                reg.idRegistroIndice = indexReg.idRegistro;
            }

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

        // Metodo per estrarre la prima data come DateTime
        static DateTime ParseFirstDate(string caption) {
            // Splitta la stringa sul carattere "-" per identificare la prima data
            var parts = caption.Split('-');
            string firstDate = parts[0];
            if (parts.Length == 1) {
                if (firstDate.Contains("/")) {
                    // Singola data
                    return DateTime.ParseExact(firstDate, "dd/MM/yyyy", CultureInfo.InvariantCulture);
                }
                return DateTime.ParseExact(firstDate, "yyyy", CultureInfo.InvariantCulture);


            }
            else {
                // Intervallo, prendi la prima data
                if (firstDate.Contains("/")) {
                    // Singola data
                    return DateTime.ParseExact(firstDate, "dd/MM/yyyy", CultureInfo.InvariantCulture);
                }
                return DateTime.ParseExact(firstDate, "yyyy", CultureInfo.InvariantCulture);
            }
        }

        void rielaboraNomi(TreeNode parent) {
            if (parent == null)
                return;
            var allChildNodes = (from TreeNode n in parent.Nodes select n).ToList();
            var ordereddNodes = allChildNodes.OrderBy(node => ParseFirstDate(node.Text)).ToList();
            parent.Nodes.Clear();
            foreach (var n in ordereddNodes) {
                parent.Nodes.Add(n);
            }

        }

        private void rielaboraNomiToolStripMenuItem_Click(object sender, EventArgs e) {
            var parent = treeMain.SelectedNode;
            rielaboraNomi(parent);
        }

        private void btnCancellaPiccoli_Click(object sender, EventArgs e) {
            new Task(() => {
                foreach (var fPath in Directory.EnumerateFiles("data", "*.jpg")) {

                    var f = new FileInfo(fPath);
                    if (f.Length < 10000) {
                        if (savedFiles.Contains(fPath.Replace("data\\", "").Replace(".jpg", ""))) {
                            savedFiles.Remove(fPath.Replace("data\\", "").Replace(".jpg", ""));
                        }

                        System.IO.File.Delete(fPath);
                        continue;
                    }


                    var imageToDisplay = Image.FromFile(fPath);
                    if (imageToDisplay.Height < 1000 || imageToDisplay.Width < 1000) {
                        imageToDisplay.Dispose();
                        if (savedFiles.Contains(fPath.Replace("data\\", "").Replace(".jpg", ""))) {
                            savedFiles.Remove(fPath.Replace("data\\", "").Replace(".jpg", ""));
                        }

                        System.IO.File.Delete(fPath);
                        continue;
                    }

                    imageToDisplay.Dispose();

                }

            }).Start();
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

        private void btnContrasta_Click(object sender, EventArgs e) {
            var bmp = pic.Image;
            processImage((Bitmap)bmp, contrastBar.Value);
            pic.Refresh();
        }

        void viewImage(int n) {
            if (n == 0) {
                return;
            }
            string fPath = loadImage(n);
            if (fPath == null) {
                return;
            }
            imageToDisplay = Image.FromFile(fPath);

            if (imageToDisplay == null)
                return;
            if (chkContrast.Checked)
                processImage((Bitmap)imageToDisplay, contrastBar.Value);
            displayImage(imageToDisplay);
            pic.Refresh();
        }

        void reContrast() {
            int n = getNum();
            viewImage(n);

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

        class TinyUrlResponse {
            public string original_url { get; set; }
            public string shortened_url { get; set; }
            public string created_at { get; set; }
        }

        // tiny url api token 

        protected async Task<T> ShortenUrl_Command<T>(string commandUrl, Dictionary<string, string> parameters = null, HttpMethod httpMethod = null) {
            commandUrl = " https://tinyurl.com/" + commandUrl;
            if (httpMethod == null) {
                httpMethod = HttpMethod.Get;
            }

            if (parameters != null && httpMethod == HttpMethod.Get) {
                StringBuilder parms = new StringBuilder();
                parms.Append("?");
                int itemCount = 0;
                foreach (KeyValuePair<string, string> item in parameters) {
                    parms.Append(item.Key);
                    parms.Append("=");
                    parms.Append(WebUtility.UrlEncode(item.Value));
                    itemCount++;
                    if (itemCount != parameters.Count) {
                        parms.Append("&");
                    }
                }

                commandUrl += parms;
            }

            HttpRequestMessage request = new HttpRequestMessage(httpMethod, commandUrl);
            if (parameters != null && httpMethod != HttpMethod.Get) {
                request.Content = new StringContent(JsonConvert.SerializeObject(parameters), Encoding.UTF8, "application/json");
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tinyUrlCode);
            HttpClient httpClient = new HttpClient();
            HttpResponseMessage res = await httpClient.SendAsync(request).ConfigureAwait(continueOnCapturedContext: false);
            res.EnsureSuccessStatusCode();
            var result = JsonConvert.DeserializeObject<T>(await res.Content.ReadAsStringAsync().ConfigureAwait(continueOnCapturedContext: false));
            request.Dispose();
            res.Dispose();
            return result;
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

        private async void shortenUrlToolStripMenuItem_Click(object sender, EventArgs e) {
            //8068db688a4614632c0a6050c033bc82a2c75b67
            if (txtPageNum.Text.Trim() == "" || txtPageNum.Text == "0")
                return;
            //var longUrl = "http://dl.antenati.san.beniculturali.it/gallery2/main.php?g2_view=core.DownloadItem&g2_itemId=" +txtPageNum.Text;

            RegNode rn = treeMain.SelectedNode?.Tag as RegNode;

            //string longUrl = rn.archiveNode.href + "/" + txtCode.Text;



            var longUrl = getImageUrlByCode(txtCode.Text);
            //Clipboard.SetText(ShortenUrl(longUrl));

            Clipboard.SetText(await ShortenUrl(longUrl));
            pushVisited();
        }

        private void contextMenuStrip1_Opening(object sender, CancelEventArgs e) {

        }

        private void upsideDownToolStripMenuItem_Click(object sender, EventArgs e) {

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
            viewImage(currentImage);
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
            string pattern = @"an_ua(\d+)";

            Match match = Regex.Match(txtWebAddress.Text, pattern);
            if (match.Success) {
                string codice = match.Groups[1].Value;
                var idRegistro = codice;
                if (idRegistro != null) {
                    setRegister(idRegistro);
                    var nn = RegNode.addArchiveNodeToTree(Registro.registroById[idRegistro], treeMain.Nodes);
                    if (nn != null) {
                        nn.EnsureVisible();
                        treeMain.SelectedNode = nn;
                    }
                }
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
                var parts = url.Split('/');
                if (parts.Length > 5) {
                    idRegistro = parts[5].Substring(5);
                    if (!Registro.registroById.ContainsKey(idRegistro)) {
                        MessageBox.Show("Manca il registro " + idRegistro + ", lo copio nel codice registro.");
                        txtCodiceRegistro.Text = idRegistro;
                        return;
                    }
                    txtUrlToDecode.Text = url;
                    urlRequested = url;
                    toMarkImportant = true;
                    gotoRegister(idRegistro);
                    if (parts.Length >= 7) {
                        goToCode(parts[6], false);
                    }
                }
            }

            if (url.StartsWith("https://iiif-antenati.cultura.gov.it/iiif")) {
                // https://iiif-antenati.cultura.gov.it/iiif/2/wOxV6Vr/full/full/0/default.jpg
                var parts = url.Split('/');
                string imgCode = parts[5].Trim();
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
        void addImportanteImage() {
            if (getMark() == null)
                return;
            var fileName = getMark() + ".jpg";
            string source = loadImage(getNum());
            if (source == null)
                return;
            System.IO.File.Copy(source, Path.Combine("important", fileName), true);
        }
        void removeImportanteImage() {
            if (getMark() == null)
                return;
            var fileName = getMark() + ".jpg";
            System.IO.File.Delete(Path.Combine("important", fileName));

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
                    addImportanteImage();
                    Console.Beep(400, 300);
                }
                else {
                    System.Media.SystemSounds.Beep.Play();
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
                        addImportanteImage();
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
                    System.Media.SystemSounds.Beep.Play();
                    if (parts[parts.Length - 2].Contains(", indice") || parts[parts.Length - 2].Contains(", indici")) {
                        parts[parts.Length - 2] = parts[parts.Length - 2].Replace(", indice", "").Replace(", indici", "");
                        if (!setNodePath(treeMain, String.Join("/", parts.ToList()))) {  // va al registro se possibile
                            //Se non è possibile torna alla posizione di partenza
                            setNodePath(treeMain, path);
                            Console.Beep(400, 500);
                            moved = false;
                        }
                    };
                }

                toPushHistory = moved;
            }
        }
        private void annoPrecedenteToolStripMenuItem_Click(object sender, EventArgs e) {
            addDeltaAnnoToCurrent(-1);
        }

        private void annoSuccessivoToolStripMenuItem_Click(object sender, EventArgs e) {
            addDeltaAnnoToCurrent(1);
        }

        private void annoSuccessivoToolStripMenuItem1_Click(object sender, EventArgs e) {
            addDeltaAnnoToCurrent(1);
        }

        private void annoPrecedenteToolStripMenuItem1_Click(object sender, EventArgs e) {
            addDeltaAnnoToCurrent(-1);
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

        string gedFileName = null;
        GedcomDatabase database = null;

        bool readGedFile(string fileName) {
            GedcomRecordReader r = new GedcomRecordReader();
            bool valid = r.ReadGedcom(fileName);
            if (valid) {
                gedFileName = fileName;
                database = r.Database;
                database.BuildSurnameList();
                return true;
            }
            else {
                MessageBox.Show("Errore", "File Gedcom non valido");
                return false;
            }
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
        string shortenName(string name) {
            if (chkFullNames.Checked)
                return name;
            var parts = name.Split(' ', ',');
            return parts[0];
        }
        string getName(GedcomIndividualRecord indi) {
            if (indi == null)
                return "";
            if (indi.Names.Count > 0) {
                var name = indi.Names[0];
                return shortenName(name.Given);
            }
            return "no name";
        }


        string getNameSurname(GedcomIndividualRecord indi) {
            if (indi == null)
                return "";
            if (indi.Names.Count > 0) {
                var name = indi.Names[0];
                if (String.IsNullOrEmpty(name.SurnamePrefix)) {
                    return String.Join(" ", new string[] { shortenName(name.Given), name.Surname });
                }
                else {
                    return String.Join(" ", new string[] { shortenName(name.Given), name.SurnamePrefix, name.Surname });
                }

                //return name.Given + " " + name.Surname;
            }
            return "no name";
        }
        IEnumerable<GedcomIndividualRecord> getSpouses(GedcomIndividualRecord indi) {
            foreach (var spouse in indi.SpouseIn) {
                var famID = spouse.Family;
                if (famID is null)
                    continue;
                var fam = (GedcomFamilyRecord)database[famID];
                if (fam.Husband != null && fam.Husband != indi.XRefID)
                    yield return database[fam.Husband] as GedcomIndividualRecord;
                if (fam.Wife != null && fam.Wife != indi.XRefID)
                    yield return database[fam.Wife] as GedcomIndividualRecord;
            }
        }
        string getMarriageDate(GedcomFamilyRecord fam, bool recursive) {
            if (fam.Marriage == null)
                return "";
            string married = getYear(fam.Marriage.Date?.DateString);
            if (String.IsNullOrEmpty(married))
                return "";
            if (recursive) {
                return " married in " + married;
            }
            else {
                return "[" + married +"]";
            }            
            
        }
        string getMarriage(GedcomIndividualRecord indi1, GedcomIndividualRecord indi2, bool recursive) {
            foreach (var spouse in indi1.SpouseIn) {
                var famID = spouse.Family;
                if (famID is null)
                    continue;
                var fam = (GedcomFamilyRecord)database[famID];
                if (fam.Husband != null && fam.Husband == indi2.XRefID)
                    return getMarriageDate(fam, recursive);
                if (fam.Wife != null && fam.Wife == indi2.XRefID)
                    return getMarriageDate(fam, recursive);
            }
            return "";
        }
        string getYear(string D) {
            if (String.IsNullOrEmpty(D))
                return "";
            var parts = D.Split(' ');
            return parts[parts.Length - 1];

        }
        string getBornDeath(GedcomIndividualRecord indi) {
            string S = "";
            if (indi.Birth != null && indi.Birth.Date != null) {
                S += chkFullDate.Checked ? indi.Birth.Date.DateString : getYear(indi.Birth.Date.DateString);
            }
            if (indi.Death != null && indi.Death.Date != null) {
                if (S != "")
                    S += "-";
                S += chkFullDate.Checked ? indi.Death.Date.DateString : getYear(indi.Death.Date.DateString);
            }
            if (S != "")
                S = $"({S})";
            return S;
        }

       
        bool isPrefix(string S) {
            if (string.IsNullOrEmpty(S))
                return false;
            if (S.Length < 3)
                return true;
            if (S.Length > 3)
                return false;
            S = S.ToLower();
            if (S == "del")
                return true;
            return false;
        }
        string getSurname(string s) {
            if (s == null)
                return null;
            var parts = s.Trim().Split(' ');
            if (parts.Length == 0)
                return "";
            if (parts[0] == "?") {
                int i = s.IndexOf("?");
                return s.Substring(i + 1).Trim();
            }
            if (parts[parts.Length - 1] == "?")
                return "?";

            if (parts.Length > 1 && isPrefix(parts[parts.Length - 2])) {
                return parts[parts.Length - 2].Trim() + " " + parts[parts.Length - 1].Trim();
            }

            if (parts.Length > 1)
                return parts[parts.Length - 1].Trim();
            return s.Trim();
        }
        string getNames(string s) {
            if (s == null)
                return null;
            var parts = s.Trim().Split(' ').ToList();

            if (parts.Count == 0)
                return "";
            if (parts[0] == "?")
                return "?";

            if (parts.Count > 1 && isPrefix(parts[parts.Count - 2])) {
                parts.RemoveRange(parts.Count - 2, 2);
            }
            else if (parts.Count > 1) {
                parts.RemoveAt(parts.Count - 1);
            }
            else if (parts.Count == 1) {
                return "?";
            }
            return String.Join(" ", parts);
        }

        GedcomIndividualRecord findByXref(string xref) {
            try {
                return database[xref] as GedcomIndividualRecord;
            }
            catch { return null; }
        }
        string WifeOrHusband(GedcomIndividualRecord indi, bool recursive) {
            string S = "";
            foreach (var spouse in getSpouses(indi)) {
                if (spouse.Sex == GedcomSex.Male) {
                    S += ", moglie di ";
                }
                else {
                    S += ", marito di ";
                }
                S += getNameSurname(spouse) + getBornDeath(spouse);
                if (chkFullKey.Checked && recursive) {
                    S += " " + spouse.XRefID + " ";
                }
                S += getMarriage(indi, spouse, recursive);

            }
            return S;
        }
        string parents(GedcomIndividualRecord indi, bool recursive) {
            string S = "";
            foreach (var childFam in indi.ChildIn) {
                if (recursive) {
                    S = S + "\r\n";
                }
                var fam = (GedcomFamilyRecord)database[childFam.Family];
                if (fam.Husband != null) {
                    var HusbandIndi = (GedcomIndividualRecord)database[fam.Husband];
                    if (recursive)
                        S += "    ";
                    S += " DI " + getName(HusbandIndi) + getBornDeath(HusbandIndi);
                    if (chkFullKey.Checked && recursive) {
                        S += " " + HusbandIndi.XRefID + " ";
                    }
                }
                if (fam.Wife != null) {
                    var WifeIndi = (GedcomIndividualRecord)database[fam.Wife];
                    string wifeName = getNameSurname(WifeIndi).Trim();
                    if (String.IsNullOrEmpty(wifeName))
                        continue;

                    if (fam.Husband != null) {
                        S += " E";
                    }
                    else {
                        if (recursive) S += "    ";
                    }
                    S += " DI " + wifeName + getBornDeath(WifeIndi);
                    if (chkFullKey.Checked && recursive) {
                        S += " " + WifeIndi.XRefID + " ";
                    }
                }                
                S += getMarriageDate(fam,recursive);
                if (recursive && fam.Husband!=null) {
                    S += parents((GedcomIndividualRecord)database[fam.Husband], true);
                }                
            }
            return S;
        }
        string children(GedcomIndividualRecord indi) {
            string S = "";
            foreach (var childFam in indi.SpouseIn) {
                var fam = (GedcomFamilyRecord)database[childFam.Family];
                if (fam.Husband != null && fam.Husband!=indi.XRefID) {
                    var HusbandIndi = (GedcomIndividualRecord)database[fam.Husband];
                    S += "\r\n with " + getNameSurname(HusbandIndi) +":\r\n";
                }
                if (fam.Wife != null && fam.Wife!= indi.XRefID) {
                    var WifeIndi = (GedcomIndividualRecord)database[fam.Wife];
                    S += "\r\n with " + getNameSurname(WifeIndi)  + ":\r\n";
                }
                foreach(var chId in fam.Children) {
                    var child = (GedcomIndividualRecord)database[chId];
                    S += "    ";
                    S += getIndiName(child);
                    if (chkFullKey.Checked) {
                        S += " " + child.XRefID + " ";
                    }
                    S += getBornDeath(child);
                    S += WifeOrHusband(child,true);
                    S += "\r\n";
                }
                if (fam.Children.Count == 0) {
                    S += "    no Children";
                    S += "\r\n";
                }
                
            }
            return S;
        }
        string getIndiName(GedcomIndividualRecord indi) {
            string S = "";
            foreach (var n in indi.Names) {
                if (String.IsNullOrEmpty(n.SurnamePrefix)) {
                    S += shortenName(n.Given) + " " + n.Surname;
                }
                else {
                    S += shortenName(n.Given) + " " + n.SurnamePrefix + " " + n.Surname;
                }
            }
            return S;
        }

        string getSimpleDescr(GedcomIndividualRecord indi) {
            string S = "";
            if (chkKeys.Checked) {
                S += indi.XRefID + " ";
            }
            S += getIndiName(indi);
            S += getBornDeath(indi);
            S += parents(indi,false);
            if (chkShowSpouse.Checked) {
                S += WifeOrHusband(indi,false);
            }
            return S;
        }

        string getLongDescr(GedcomIndividualRecord indi) {
            string S = "";
            
            S += getIndiName(indi);
            S += getBornDeath(indi);
            if (chkFullKey.Checked) {
                S += "     "+ indi.XRefID;
            }
            S += "\n\r";
            if (chkShowSpouse.Checked) {
                S += WifeOrHusband(indi,true);
                S += "\n\r";
            }
            S += parents(indi, true);
            S += "\n\r";
            S += children(indi);

            return S;
        }

        //Michele, Maria Sparano DI Giuseppe di Filippo E DI Maria Concetta marito di Francesca Giovanna Adelmi
        //Michele, Maria Sparano DI Giuseppe di Filippo E DI Maria Concetta moglie di Francesco Giovanna Adelmi
        IEnumerable<GedcomIndividualRecord> findIndividual(string searchMask) {
            string name = null;
            string surname = null;
            string fatherName = null;
            string grandFatherName = null;
            string motherFatherName = null;
            string motherNameSurname = null;
            string wifeNameSurname = null;
            if (searchMask.StartsWith("DI")) {
                searchMask = " " + searchMask;
            }

            var coniugeDi = searchMask.Split(new string[] { " marito di ", " moglie di " }, StringSplitOptions.None);
            if (coniugeDi.Length > 1) {
                wifeNameSurname = coniugeDi[1]?.Trim();
                searchMask = coniugeDi[0]?.Trim();
            }
            var motherParts = searchMask.Split(new string[] { " E DI " }, StringSplitOptions.None);
            if (motherParts.Length > 1) {
                motherNameSurname = motherParts[1]?.Trim();
                searchMask = motherParts[0];
                var tryFatherMothers = motherNameSurname.Split(new string[] { " DI " }, StringSplitOptions.None);
                if (tryFatherMothers.Length > 1) {
                    motherFatherName = tryFatherMothers[1]?.Trim();
                    motherNameSurname = tryFatherMothers[0]?.Trim();
                }
            }

            var fatherParts = searchMask.Split(new string[] { " DI " }, StringSplitOptions.None);
            if (fatherParts.Length > 1) {
                fatherName = fatherParts[1]?.Trim();
                searchMask = fatherParts[0]?.Trim();
                if (fatherParts.Length > 2) {
                    grandFatherName = fatherParts[2]?.Trim();
                }
            }


            name = getNames(searchMask)?.Trim();
            surname = getSurname(searchMask)?.Trim();

            var mainParts = searchMask.Split(' ');
            if (mainParts.Length == 1 && fatherName != null) {
                //verifica se ha messo il cognome nel padre, sistema le cose
                var fatherNameTry = getNames(fatherName)?.Trim();
                var fatherSurnameTry = getSurname(fatherName)?.Trim();
                if (fatherNameTry != "?" && fatherSurnameTry != "?") {
                    name = surname;
                    surname = fatherSurnameTry;
                    fatherName = fatherNameTry;
                }


            }

            if (surname != null && surname != "?" && !database.Surnames.ContainsKey(surname) && name == null) {
                name = surname;
                surname = "?";
            }

            if (surname != null && surname != "?" && !database.Surnames.ContainsKey(surname) && name != null && database.Surnames.ContainsKey(name)) {
                var x = name;
                name = surname;
                surname = x;
            }

            string motherName = motherNameSurname != null ? getNames(motherNameSurname)?.Trim() : null;
            string motherSurname = motherNameSurname != null ? getSurname(motherNameSurname)?.Trim() : null;
            if (motherSurname != null && motherSurname != "?" && !database.Surnames.ContainsKey(motherSurname) && motherName == "?") {
                motherName = motherSurname;
                motherSurname = "?";
            }
            if (motherSurname != null && motherSurname != "?" && motherName != null && motherName != "?") {
                if (!database.Surnames.ContainsKey(motherSurname) && database.Surnames.ContainsKey(motherName)) {
                    var x = motherName;
                    motherName = motherSurname;
                    motherSurname = x;
                }
            }

            string wifeName = wifeNameSurname != null ? getNames(wifeNameSurname)?.Trim() : null;
            string wifeSurname = wifeNameSurname != null ? getSurname(wifeNameSurname)?.Trim() : null;
            if (wifeSurname != null && wifeSurname != "?" && !database.Surnames.ContainsKey(wifeSurname) && wifeName == "?") {
                wifeName = wifeSurname;
                wifeSurname = "?";
            }
            if (wifeSurname != null && wifeSurname != "?" && wifeName != null && wifeName != "?") {
                if (!database.Surnames.ContainsKey(wifeSurname) && database.Surnames.ContainsKey(wifeName)) {
                    var x = wifeName;
                    wifeName = wifeSurname;
                    wifeSurname = x;
                }
            }

            foreach (var indi in database.Individuals) {
                if (!String.IsNullOrEmpty(name) && name != "?") {
                    if (!indi.MatchFirstname(name, false))
                        continue;
                }
                if (!String.IsNullOrEmpty(surname) && surname != "?") {
                    if (!indi.MatchSurname(surname, false))
                        continue;
                }

                if (wifeNameSurname != null) {
                    bool foundWife = false;
                    foreach (var spouse in getSpouses(indi)) {
                        if (wifeName != null & wifeName != "?") {
                            if (!spouse.MatchFirstname(wifeName, false))
                                continue;
                        }
                        if (wifeSurname != null & wifeSurname != "?") {
                            if (!spouse.MatchSurname(wifeSurname, false))
                                continue;
                        }
                        foundWife = true;
                        break;
                    }
                    if (!foundWife)
                        continue;
                }

                if (fatherName != null) {
                    bool foundFather = false;
                    foreach (var childFam in indi.ChildIn) {
                        var fam = (GedcomFamilyRecord)database[childFam.Family];
                        if (fam.Husband != null) {
                            var FatherIndi = (GedcomIndividualRecord)database[fam.Husband];

                            if (fatherName != null & fatherName != "?") {
                                if (!FatherIndi.MatchFirstname(fatherName, false))
                                    continue;
                            }

                            if (grandFatherName != null) { //Cerca il nome del padre del padre
                                if (FatherIndi.ChildIn == null || FatherIndi.ChildIn.Count == 0)
                                    continue;
                                var grandfam = (GedcomFamilyRecord)database[FatherIndi.ChildIn[0].Family];
                                if (grandfam.Husband == null)
                                    continue;
                                var grandFatherIndi = (GedcomIndividualRecord)database[grandfam.Husband];
                                if (!grandFatherIndi.MatchFirstname(grandFatherName, false))
                                    continue;
                            }

                            foundFather = true;
                            break;
                        }
                    }
                    if (!foundFather)
                        continue;
                }

                if (motherNameSurname != null) {
                    bool foundMother = false;
                    foreach (var childFam in indi.ChildIn) {
                        var fam = (GedcomFamilyRecord)database[childFam.Family];
                        if (fam.Wife != null) {
                            var MotherIndi = (GedcomIndividualRecord)database[fam.Wife];
                            if (motherName != null & motherName != "?") {
                                if (!MotherIndi.MatchFirstname(motherName, false))
                                    continue;
                            }
                            if (motherSurname != null & motherSurname != "?") {
                                if (!MotherIndi.MatchSurname(motherSurname, false))
                                    continue;
                            }

                            if (motherFatherName != null) {//Cerca il nome del padre della madre
                                if (MotherIndi.ChildIn == null || MotherIndi.ChildIn.Count == 0)
                                    continue;
                                var grandfam = (GedcomFamilyRecord)database[MotherIndi.ChildIn[0].Family];
                                if (grandfam.Husband == null)
                                    continue;
                                var grandFatherIndi = (GedcomIndividualRecord)database[grandfam.Husband];
                                if (!grandFatherIndi.MatchFirstname(motherFatherName, false))
                                    continue;
                            }


                            foundMother = true;
                            break;
                        }
                    }
                    if (!foundMother)
                        continue;
                }


                yield return indi;
            }



        }

        void searchNames() {
            if (database == null) { return; }
            StringBuilder sb = new StringBuilder();
            if (txtSearchName.Text == "")
                return;

            foreach (var indi in findIndividual(txtSearchName.Text)) {
                sb.AppendLine(getSimpleDescr(indi));
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
            Console.Beep(100, 100);
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
            string xref = txtID.Text.Trim();
            if (String.IsNullOrEmpty(xref))
                return;
            if (!xref.StartsWith("XREF"))
                xref = "XREF" + xref;
            searchID(xref);
        }
        private void txtID_Leave(object sender, EventArgs e) {
            longSearch();
        }
        void searchID(string xref) {
            var indi = findByXref(xref);
            if (indi == null) return;
            txtLongFam.Text = getLongDescr(indi);
        }

 


        private void chkKeys_CheckedChanged(object sender, EventArgs e) {
            searchNames();
        }

        private void fullKey_CheckedChanged(object sender, EventArgs e) {
            longSearch();
        }
        
	}



	public class RegistryExtractor {
        private static readonly HttpClient client = new HttpClient();

        public static async Task ExtractRegistryInfo(string url) {
            var html = await client.GetStringAsync(url);
            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(html);

            var detailRegistryNode = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'detail-registry')]");

            if (detailRegistryNode != null) {
                // Estrai nome archivio
                var archiveNode = detailRegistryNode.SelectSingleNode("//p[strong[contains(text(), 'Conservato da:')]]/a");
                string archiveName = archiveNode?.InnerText.Trim();

                // Estrai tipo registro
                var typeNode = detailRegistryNode.SelectSingleNode("//div/p/a/strong");
                string type = typeNode?.InnerText.Trim();

                // Estrai nome registro
                var registryNode = detailRegistryNode.SelectSingleNode("//p/a[contains(@href, 'descrizione=Stato')]");
                string registryName = registryNode?.InnerText.Trim();

                // Estrai comune/località
                var locationNode = detailRegistryNode.SelectSingleNode("//p[strong[contains(text(), 'Comune/Località:')]]/a");
                string location = locationNode?.InnerText.Trim();

                // Anno (ti anticipo dove lo cerchiamo: verifica se è in fondo alla pagina o vicino all’elemento "Copialink del bookmark")
                var yearNode = detailRegistryNode.SelectSingleNode("//aside[contains(@class, 'gap-pv')]//text()[contains(., '1816')]");
                string year = yearNode?.InnerText.Trim();

                Console.WriteLine($"Archivio: {archiveName}");
                Console.WriteLine($"Tipo: {type}");
                Console.WriteLine($"Registro: {registryName}");
                Console.WriteLine($"Comune/Località: {location}");
                Console.WriteLine($"Anno: {year}");
            }
        }
    }


}
