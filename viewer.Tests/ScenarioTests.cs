using Newtonsoft.Json;
using NUnit.Framework;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace viewer.Tests {

    /// <summary>
    /// Il vero Viewer (costruttore, timer, albero) su un thread suo con la finestra invisibile.
    /// Si comanda come farebbe l'utente (doppio click su un registro, +1, Go) e se ne osserva lo stato.
    /// </summary>
    sealed class ViewerHost : IDisposable {
        readonly Thread ui;
        readonly ManualResetEventSlim shown = new ManualResetEventSlim(false);
        Exception startError;
        public Viewer V { get; private set; }

        public ViewerHost() {
            ui = new Thread(() => {
                try {
                    V = new Viewer { Opacity = 0, ShowInTaskbar = false };
                    V.Shown += (s, e) => shown.Set();
                    Application.Run(V);
                }
                catch (Exception e) { startError = e; shown.Set(); }
            }) { IsBackground = true };
            ui.SetApartmentState(ApartmentState.STA);
            ui.Start();
            if (!shown.Wait(20000)) throw new TimeoutException("il Viewer non si e' aperto");
            if (startError != null) throw new Exception("costruttore del Viewer", startError);
        }

        public T Get<T>(Func<T> f) => (T)V.Invoke(f);
        public void Do(Action a) => V.Invoke(a);

        public int Shown => Get(() => (int)Priv.Get(V, "nEffettivo"));
        public string PageBox => Get(() => ((TextBox)Priv.Get(V, "txtPageNum")).Text);

        public void Next() => Do(() => Priv.Call(V, "btnPiuUno_Click", null, EventArgs.Empty));
        public void Go() => Do(() => Priv.Call(V, "btnGo_Click", null, EventArgs.Empty));

        /// <summary>doppio click sul nodo del registro, come treeMain_MouseDown</summary>
        public void OpenRegister(string idRegistro) => Do(() => {
            var node = (TreeNode)Priv.Call(V, "searchByRegistro", idRegistro, null);
            if (node == null) throw new InvalidOperationException("registro " + idRegistro + " non nell'albero");
            ((TreeView)Priv.Get(V, "treeMain")).SelectedNode = node;
            Priv.Call(V, "exploreMainNodeAsync", node);
        });

        public void WaitUntil(Func<bool> cond, int ms, string what) {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!cond()) {
                if (sw.ElapsedMilliseconds > ms) Assert.Fail($"{what}: non successo entro {ms} ms ({State()})");
                Thread.Sleep(50);
            }
        }

        /// <summary>stato leggibile, per i messaggi d'errore</summary>
        public string State() {
            try {
                return Get(() => {
                    string id = (string)Priv.Get(V, "idRegistro");
                    bool known = Registro.registroById.TryGetValue(id ?? "", out Registro r);
                    return $"registro {id}, richiesta {((TextBox)Priv.Get(V, "txtPageNum")).Text}, a video {Priv.Get(V, "nEffettivo")}, " +
                           (known ? $"ultima pagina salvata '{r.lastPageViewed ?? "null"}'" : $"registro NON in registroById ({Registro.registroById.Count} voci)");
                });
            }
            catch (Exception e) { return "stato non leggibile: " + e.Message; }
        }

        public void Dispose() {
            // BeginInvoke, non Invoke: se il thread della finestra e' bloccato (test che lo verifica)
            // la chiusura non deve restare appesa; il thread e' di background e muore col processo
            try { if (V != null && !V.IsDisposed) V.BeginInvoke(new Action(V.Close)); } catch { }
            ui.Join(10000);
        }

        /// <summary>
        /// Esegue azione sul thread della finestra e dice se e' tornata entro ms
        /// (false = finestra bloccata)
        /// </summary>
        public bool RunOnUi(Action action, int ms) => V.BeginInvoke(action).AsyncWaitHandle.WaitOne(ms);
    }

    /// <summary>
    /// Scenari d'uso reali, quelli dei problemi del 17-19/10/2026, sul Viewer vero contro un server
    /// locale che imita iiif-antenati (ritardi ed errori per pagina). Nessun accesso a internet.
    /// </summary>
    [TestFixture, NonParallelizable, CancelAfter(90000)]
    public class ScenarioTests {
        LocalHttpServer server;
        string savedIiif, savedWarm, savedDir;
        StringWriter log;
        TextWriter syncLog, savedOut;

        /// <summary>
        /// Testo del log letto sotto lo stesso lucchetto delle scritture (il wrapper Synchronized si blocca
        /// su se stesso): leggere log.ToString() mentre i thread del Viewer scrivevano faceva lanciare
        /// ogni tanto ArgumentOutOfRangeException allo StringBuilder interno
        /// </summary>
        string LogText() { lock (syncLog) { return log.ToString(); } }
        ViewerHost host;
        readonly ConcurrentDictionary<string, int> delayMs = new ConcurrentDictionary<string, int>();
        readonly ConcurrentDictionary<string, int> status = new ConcurrentDictionary<string, int>();

        /// <summary>
        /// Fine del test: le richieste ancora in ritardo rispondono subito con un errore. Senza, un download
        /// lento lasciato dal test finiva dopo e il Viewer chiuso salvava l'immagine in data\ relativo alla
        /// cartella corrente, cioe' nella sandbox del test successivo, dove la pagina risultava gia' in cache
        /// (PiuUno_IlPrecaricamentoSiSpostaConIlLettore falliva cosi', a seconda di quali test lo precedevano)
        /// </summary>
        ManualResetEventSlim ending;

        static readonly byte[] Jpeg = MakeJpeg();
        static byte[] MakeJpeg() {
            var rnd = new Random(7);
            using (var b = new Bitmap(300, 300)) {
                for (int y = 0; y < 300; y++)
                    for (int x = 0; x < 300; x++)
                        b.SetPixel(x, y, Color.FromArgb(rnd.Next(256), rnd.Next(256), rnd.Next(256)));
                using (var ms = new MemoryStream()) { b.Save(ms, ImageFormat.Jpeg); return ms.ToArray(); }
            }
        }

        // due registri della stessa tipologia, 12 pagine ciascuno, manifest gia' su disco
        const string R1 = "900001", R2 = "900002";
        static string Code(string reg, int page) => (reg == R1 ? "wA" : "wB") + page.ToString("00") + "x";
        static IEnumerable<int> Pages => Enumerable.Range(1, 12);

        [SetUp]
        public void SetUp() {
            var end = ending = new ManualResetEventSlim(false);   // di questo test: il server lo cattura
            savedDir = Environment.CurrentDirectory;
            Environment.CurrentDirectory = Sandbox.NewDir();      // start.txt, note, data\, manifest\ di questo scenario
            File.WriteAllText(Archivio.archiveName, "[]");
            Tree.Reset();
            Tree.Bari();
            Tree.AddAnno();
            Tree.AddKind();
            foreach (var (id, man, seg) in new[] { (R1, "manA", "Parte 1"), (R2, "manB", "Parte 2") }) {
                var r = Tree.AddRegistro(idRegistro: id, segnatura: seg);
                r.manifestId = man;
                Directory.CreateDirectory("manifest");
                File.WriteAllText(Manifest.manifestFileName(man), JsonConvert.SerializeObject(new {
                    idRegistro = id,
                    mapPage = Pages.ToDictionary(p => p, p => Code(id, p)),
                    pageWidth = Pages.ToDictionary(p => Code(id, p), p => 2000),
                }));
            }

            server = new LocalHttpServer { Concurrent = true };
            server.Handler = req => {
                string path = req.Split(' ')[1];
                var m = Regex.Match(path, @"/iiif/2/(\w+)/");
                if (!m.Success) return ("HTTP/1.1 200 OK\r\nContent-Type: text/html\r\n", Encoding.ASCII.GetBytes("home"));
                string code = m.Groups[1].Value;
                if (delayMs.TryGetValue(code, out int d)) end.Wait(d);
                if (end.IsSet)
                    return ("HTTP/1.1 503 Fine del test\r\nContent-Type: text/html\r\n", Encoding.ASCII.GetBytes("<html>fine</html>"));
                if (status.TryGetValue(code, out int st) && st != 200)
                    return ($"HTTP/1.1 {st} Errore\r\nContent-Type: text/html\r\n", Encoding.ASCII.GetBytes("<html>errore</html>"));
                return ("HTTP/1.1 200 OK\r\nContent-Type: image/jpeg\r\n", Jpeg);
            };
            savedIiif = Viewer.iiifBaseUrl; Viewer.iiifBaseUrl = server.BaseUrl + "iiif/2/";
            savedWarm = PageLoader.warmUpUrl; PageLoader.warmUpUrl = server.BaseUrl;

            savedOut = Console.Out;
            log = new StringWriter();
            syncLog = TextWriter.Synchronized(log);
            Console.SetOut(syncLog);
        }

        [TearDown]
        public void TearDown() {
            ending.Set();
            host?.Dispose(); host = null;
            Console.SetOut(savedOut);
            Viewer.iiifBaseUrl = savedIiif;
            PageLoader.warmUpUrl = savedWarm;
            server.Dispose();
            Environment.CurrentDirectory = savedDir;
            delayMs.Clear(); status.Clear();
        }

        void Start(string registro, int page, int cache = 5) {
            File.WriteAllText("start.txt", $"cache§{cache}\r\ndimension§1000\r\nregistro§{registro}\r\npageNum§{page}\r\n", Encoding.UTF8);
            host = new ViewerHost();
        }

        void Slow(string reg, int ms) { foreach (int p in Pages) delayMs[Code(reg, p)] = ms; }

        int TimesShown(int page) => Regex.Matches(LogText(), $@"\] Pagina {page} a video").Count;
        int Requests(string code) => server.Requests.Count(r => r.Contains("/" + code + "/"));

        [Test]
        public void Avvio_LaPaginaSalvataVaAVideo() {
            // 18/10: la pagina iniziale non partiva finche' non si cliccava fuori dal numero di pagina
            Start(R1, 3);
            host.WaitUntil(() => host.Shown == 3, 10000, "pagina 3 a video all'avvio");
            Assert.That(TimesShown(3), Is.EqualTo(1));
        }

        [Test]
        public void PiuUnoRipetuto_LePagineArrivanoUnaVoltaSola() {
            // 17-18/10: pagina 1 ferma per 20 s dopo +1; ogni pagina disegnata due volte
            Slow(R1, 200);
            Start(R1, 1);
            host.WaitUntil(() => host.Shown == 1, 10000, "pagina 1 a video");
            for (int i = 0; i < 5; i++) { host.Next(); Thread.Sleep(400); }
            host.WaitUntil(() => host.Shown == 6, 10000, "pagina 6 a video dopo cinque +1");
            for (int p = 1; p <= 6; p++)
                Assert.That(TimesShown(p), Is.LessThanOrEqualTo(1), $"pagina {p} disegnata piu' di una volta");
            Assert.That(Pages.Max(p => Requests(Code(R1, p))), Is.EqualTo(1), "ogni immagine scaricata una volta sola");
        }

        [Test]
        public void DoppioClickSuAltroRegistro_IlSuoPrecaricamentoParteSubito() {
            // 19/10: con il precaricamento del registro precedente in corso, quello nuovo non partiva
            Slow(R1, 3000);
            Slow(R2, 100);
            Start(R1, 1);
            host.WaitUntil(() => Requests(Code(R1, 2)) == 1, 10000, "precaricamento di R1 in corso");

            host.OpenRegister(R2);
            host.WaitUntil(() => host.Shown == 1 && host.Get(() => (string)Priv.Get(host.V, "idRegistro")) == R2, 10000, "R2 pagina 1 a video");
            host.WaitUntil(() => Requests(Code(R2, 2)) == 1 && Requests(Code(R2, 3)) == 1, 2000, "precaricamento di R2 partito");
        }

        [Test]
        public void PiuUno_IlPrecaricamentoSiSpostaConIlLettore() {
            // Server lento: il precaricamento partito da pagina 2 ha 2,3,4 in volo. Andando avanti, quello
            // nuovo deve chiedere subito la pagina 5 invece di aspettare che il vecchio liberi un posto
            // (prima un precaricamento in corso impediva di avviarne un altro)
            // 8 s per immagine: col comportamento sbagliato la 5 partirebbe solo dopo ~8 s (quando il
            // vecchio precaricamento libera un posto); il limite di 3 s lascia margine a una macchina carica
            Slow(R1, 8000);
            delayMs[Code(R1, 1)] = 0;
            Start(R1, 1);
            host.WaitUntil(() => host.Shown == 1, 10000, "pagina 1");
            host.WaitUntil(() => Requests(Code(R1, 4)) == 1, 3000, "precaricamento 2-4 in volo");
            Assert.That(Requests(Code(R1, 5)), Is.EqualTo(0));

            host.Next(); host.Next();                       // pagina 3
            host.WaitUntil(() => Requests(Code(R1, 5)) == 1, 3000, "pagina 5 chiesta subito");
            Assert.That(Requests(Code(R1, 3)), Is.EqualTo(1), "le pagine gia' in volo si riagganciano, non si richiedono");
        }

        [Test]
        public void CambiandoRegistroMentreLaPaginaArriva_SiRiapreSullUltimaVista() {
            // 19/10: l'ultima pagina si salvava solo uscendo dal nodo a pagina gia' arrivata; cambiando
            // registro mentre la successiva caricava (col server lento: quasi sempre) non restava traccia
            delayMs[Code(R1, 5)] = 8000;                    // la 5 arrivera' tardi, anche dal precaricamento
            Start(R1, 1);
            host.WaitUntil(() => host.Shown == 1, 10000, "R1 pagina 1");
            for (int i = 0; i < 3; i++) { host.Next(); host.WaitUntil(() => host.Shown == i + 2, 10000, $"R1 pagina {i + 2}"); }
            host.WaitUntil(() => host.Get(() => Registro.registroById[R1].lastPageViewed) == "4", 2000, "pagina 4 salvata come ultima vista");
            host.Next();                                    // pagina 5 richiesta, ancora in arrivo
            Thread.Sleep(500);
            Assert.That(host.Shown, Is.EqualTo(4), "la 5 non e' ancora arrivata: " + host.State());

            host.OpenRegister(R2);
            host.WaitUntil(() => host.Get(() => (string)Priv.Get(host.V, "idRegistro")) == R2 && host.Shown == 1, 10000, "R2 pagina 1");

            host.OpenRegister(R1);
            host.WaitUntil(() => host.Get(() => (string)Priv.Get(host.V, "idRegistro")) == R1 && host.Shown == 4, 10000, "R1 riaperto a pagina 4, l'ultima vista");
            Assert.That(host.PageBox, Is.EqualTo("4"));
        }

        [Test]
        public void TornandoSuUnRegistro_SiRiapreSullUltimaPaginaVista() {
            Start(R1, 1);
            host.WaitUntil(() => host.Shown == 1, 10000, "R1 pagina 1");
            for (int i = 0; i < 3; i++) { host.Next(); host.WaitUntil(() => host.Shown == i + 2, 10000, $"R1 pagina {i + 2}"); }

            host.OpenRegister(R2);
            host.WaitUntil(() => host.Get(() => (string)Priv.Get(host.V, "idRegistro")) == R2 && host.Shown == 1, 10000, "R2 pagina 1");

            host.OpenRegister(R1);
            host.WaitUntil(() => host.Get(() => (string)Priv.Get(host.V, "idRegistro")) == R1 && host.Shown == 4, 10000, "R1 riaperto a pagina 4");
            Assert.That(host.PageBox, Is.EqualTo("4"));
        }

        [Test]
        public void Contrasto_SuPaginaNonInCache_NonBloccaLaFinestra() {
            // Il gestore aspetta il download con GetAwaiter().GetResult() sul thread della finestra; il
            // download, dopo un await, vuole tornare proprio su quel thread: attesa circolare, finestra
            // bloccata per sempre. Succedeva solo con la pagina non in cache.
            delayMs[Code(R1, 7)] = 1000;
            Start(R1, 1);
            host.WaitUntil(() => host.Shown == 1, 10000, "pagina 1");
            bool returned = host.RunOnUi(() => {
                ((TextBox)Priv.Get(host.V, "txtPageNum")).Text = "7";
                Priv.Call(host.V, "btnMoreContrast_Click", null, EventArgs.Empty);
            }, 8000);
            Assert.That(returned, Is.True, "finestra bloccata dal contrasto su pagina non in cache");
            host.WaitUntil(() => host.Shown == 7, 10000, "pagina 7 a video");
        }

        [Test]
        public void Reset_SuPaginaNonInCache_NonBloccaLaFinestra() {
            delayMs[Code(R1, 8)] = 1000;
            Start(R1, 1);
            host.WaitUntil(() => host.Shown == 1, 10000, "pagina 1");
            bool returned = host.RunOnUi(() => {
                ((TextBox)Priv.Get(host.V, "txtPageNum")).Text = "8";
                Priv.Set(host.V, "currentImage", 8);
                Priv.Call(host.V, "btnReset_Click", null, EventArgs.Empty);
            }, 8000);
            Assert.That(returned, Is.True, "finestra bloccata dal Reset su pagina non in cache");
            host.WaitUntil(() => host.Shown == 8, 10000, "pagina 8 a video");
        }

        [Test]
        public void VaiAllIndice_SenzaRegistroCorrente_NessunaEccezione() {
            // "&" al posto di "&&": senza registro corrente leggeva comunque registro.idRegistroIndice.
            // Succede se il registro resta selezionato ma "vuoto" (es. manifest non caricato).
            Start(R1, 1);
            host.WaitUntil(() => host.Shown == 1, 10000, "pagina 1");
            Exception err = null;
            host.Do(() => {
                Priv.Set(host.V, "idRegistro", null);
                try { Priv.Call(host.V, "btnGoToIndex_Click", null, EventArgs.Empty); }
                catch (Exception e) { err = e; }
            });
            Assert.That(err, Is.Null, "vai all'indice senza registro corrente: " + err);
        }

        [Test]
        public void Go_DopoErroriDelServer_SbloccaLaPagina() {
            status[Code(R1, 2)] = 500;
            Start(R1, 1);
            host.WaitUntil(() => host.Shown == 1, 10000, "pagina 1");
            host.Next();
            host.WaitUntil(() => Requests(Code(R1, 2)) >= 3, 8000, "tre tentativi falliti su pagina 2");
            Thread.Sleep(600);
            int tentativi = Requests(Code(R1, 2));
            Thread.Sleep(1500);
            Assert.That(Requests(Code(R1, 2)), Is.EqualTo(tentativi), "dopo 3 fallimenti niente piu' richieste automatiche");

            status.TryRemove(Code(R1, 2), out _);          // il server torna a funzionare
            host.Go();
            host.WaitUntil(() => TimesShown(2) == 1, 10000, "pagina 2 a video dopo Go");
        }
    }
}
