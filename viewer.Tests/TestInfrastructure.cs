using NUnit.Framework;
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;

namespace viewer.Tests {

    /// <summary>
    /// Prima di qualsiasi test sposta la cartella corrente in una sandbox temporanea.
    /// I costruttori statici di Archivio/Fondo/Serie/... leggono i JSON dalla cartella corrente
    /// e Archivio, se non trova Archivi.json, scarica l'elenco da Antenati: con un Archivi.json
    /// vuoto i test restano offline e non toccano mai i dati veri in viewer\bin.
    /// </summary>
    [SetUpFixture]
    public class Sandbox {
        public static string Root { get; private set; }
        public static string FixturesDir { get; private set; }
        static string originalDir;

        [OneTimeSetUp]
        public void Enter() {
            FixturesDir = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures");
            Root = Path.Combine(Path.GetTempPath(), "viewer.Tests." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            File.WriteAllText(Path.Combine(Root, Archivio.archiveName), "[]");
            originalDir = Environment.CurrentDirectory;
            Environment.CurrentDirectory = Root;
            Viewer.soundsEnabled = false;     // niente beep mentre girano i test

            // Come Program.Main: il codice blocca su .Result (sync-over-async) e i test di concorrenza
            // lanciano decine di richieste insieme; con i thread minimi di default il pool cresce di
            // 1-2 thread al secondo e ogni richiesta resta in coda per secondi
            System.Threading.ThreadPool.SetMinThreads(64, 64);
        }

        [OneTimeTearDown]
        public void Leave() {
            Environment.CurrentDirectory = originalDir;
            try { Directory.Delete(Root, true); } catch { /* best effort */ }
        }

        public static string ReadFixture(string name) {
            return File.ReadAllText(Path.Combine(FixturesDir, name), Encoding.UTF8);
        }

        /// <summary>Cartella vuota dentro la sandbox, una per test</summary>
        public static string NewDir() {
            string d = Path.Combine(Root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }
    }

    /// <summary>
    /// Stato statico dell'albero (liste e dizionari di ogni livello) azzerato tra un test e l'altro,
    /// più costruttori di comodo per una catena Archivio > Fondo > Serie > Anno > Tipologia > Registro.
    /// </summary>
    public static class Tree {
        public static void Reset() {
            Archivio.archivi = new System.Collections.Generic.List<Archivio>();
            Archivio.archivioById.Clear();
            Fondo.fondi = new System.Collections.Generic.List<Fondo>();
            Fondo.fondoById.Clear();
            Serie.series = new System.Collections.Generic.List<Serie>();
            Serie.serieById.Clear();
            AnnoSerie.annoSeries = new System.Collections.Generic.List<AnnoSerie>();
            AnnoSerie.annoSerieById.Clear();
            AnnoSerieKind.annoSeriesKind = new System.Collections.Generic.List<AnnoSerieKind>();
            AnnoSerieKind.annoSerieKindById.Clear();
            Registro.registri = new System.Collections.Generic.List<Registro>();
            Registro.registroById.Clear();
            ((IDictionary)typeof(Manifest).GetField("allManifest", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null)).Clear();
            PageLoader.cookies = new CookieCollection();
        }

        public static Archivio AddArchivio(string id = "208", string description = "Archivio di Stato di Bari") {
            var a = new Archivio(id, null, description);
            Archivio.archivi.Add(a);
            Archivio.archivioById[id] = a;
            return a;
        }

        public static Fondo AddFondo(string idArchivio = "208", string idFondo = "281500", string description = "Stato civile napoleonico") {
            var f = new Fondo(idArchivio, idFondo, description);
            Fondo.fondi.Add(f);
            Fondo.fondoById[idFondo] = f;
            return f;
        }

        public static Serie AddSerie(string idFondo = "281500", string idSerie = "282216", string description = "Bari") {
            var s = new Serie(idFondo, idSerie, description);
            Serie.series.Add(s);
            Serie.serieById[idSerie] = s;
            return s;
        }

        public static AnnoSerie AddAnno(string idSerie = "282216", string anno = "1813") {
            var a = new AnnoSerie(idSerie, anno);
            AnnoSerie.annoSeries.Add(a);
            AnnoSerie.annoSerieById[a.key] = a;
            return a;
        }

        public static AnnoSerieKind AddKind(string idSerie = "282216", string anno = "1813", string kind = "Nati") {
            var k = new AnnoSerieKind(anno, idSerie, kind);
            AnnoSerieKind.annoSeriesKind.Add(k);
            AnnoSerieKind.annoSerieKindById[k.key] = k;
            return k;
        }

        public static Registro AddRegistro(string idSerie = "282216", string anno = "1813", string kind = "Nati",
                                           string idRegistro = "282248", string segnatura = "Parte 1") {
            var r = new Registro(anno, idSerie, kind, idRegistro, segnatura);
            Registro.registri.Add(r);
            Registro.registroById[idRegistro] = r;
            return r;
        }

        /// <summary>Catena completa fino alla serie Bari (archivio 208, fondo 281500, serie 282216)</summary>
        public static Serie Bari() {
            AddArchivio();
            AddFondo();
            return AddSerie();
        }
    }

    /// <summary>
    /// Accesso ai membri privati (soprattutto di Viewer, che e' una Form da 4000+ righe).
    /// </summary>
    public static class Priv {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        public static object Call(object target, string method, params object[] args) {
            Type t = target as Type ?? target.GetType();
            object instance = target is Type ? null : target;
            var candidates = t.GetMethods(All)
                .Where(m => m.Name == method && m.GetParameters().Length == args.Length)
                .Where(m => m.GetParameters().Zip(args, (p, a) => {
                    var pt = p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType;
                    return a == null ? !pt.IsValueType : pt.IsInstanceOfType(a);
                }).All(ok => ok))
                .ToList();
            if (candidates.Count == 0)
                throw new MissingMethodException(t.Name, method);
            try {
                return candidates[0].Invoke(instance, args);
            }
            catch (TargetInvocationException e) {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
        }

        public static T Call<T>(object target, string method, params object[] args) {
            return (T)Call(target, method, args);
        }

        public static void Set(object target, string field, object value) {
            target.GetType().GetField(field, All).SetValue(target, value);
        }

        public static object Get(object target, string field) {
            return target.GetType().GetField(field, All).GetValue(target);
        }

        /// <summary>
        /// Viewer senza eseguire il costruttore (che apre WebView2, va in rete e legge i file utente).
        /// I campi con inizializzatore restano null: chi li usa deve impostarli.
        /// Il finalizzatore viene soppresso, altrimenti Dispose su un oggetto mai inizializzato
        /// farebbe cadere il processo di test.
        /// </summary>
        public static Viewer NewViewer() {
            var v = (Viewer)FormatterServices.GetUninitializedObject(typeof(Viewer));
            GC.SuppressFinalize(v);
            return v;
        }
    }
}
