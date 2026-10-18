using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace viewer.Tests {

    /// <summary>
    /// Strutture condivise tra il thread dell'interfaccia, il caricamento della pagina, il precaricamento
    /// e l'esplorazione. Sono prove di carico: non dimostrano l'assenza di race, ma senza i lock
    /// queste raffiche di accessi contemporanei corrompono le collezioni con buona probabilita'.
    /// </summary>
    [TestFixture]
    public class ConcurrencyTests {

        [SetUp]
        public void SetUp() { Tree.Reset(); }

        [Test]
        public void Manifest_CaricatoDaPiuThread_UnaSolaIstanza() {
            string id = "c" + Guid.NewGuid().ToString("N").Substring(0, 6);
            Directory.CreateDirectory("manifest");
            var json = new JObject {
                ["sequences"] = new JArray { new JObject { ["canvases"] = new JArray {
                    new JObject { ["@id"] = "https://antenati.cultura.gov.it/ark:/12657/an_ua1/a", ["width"] = 1 } } } },
                ["metadata"] = new JArray()
            };
            File.WriteAllText(Manifest.manifestFileName(id), JsonConvert.SerializeObject(Manifest.fromJson(json)));

            using (var start = new ManualResetEventSlim(false)) {
                var tasks = Enumerable.Range(0, 32).Select(_ => Task.Run(() => {
                    start.Wait();
                    return Manifest.LoadManifest(id).Result;
                })).ToArray();
                start.Set();
                Task.WaitAll(tasks);

                Assert.That(tasks.Select(t => t.Result).Distinct().Count(), Is.EqualTo(1),
                    "tutti i chiamanti devono ricevere la stessa istanza");
            }
        }

        [Test]
        public void Cookie_AggiornatiDaPiuThread() {
            using (var server = new LocalHttpServer()) {
                int n = 0;
                // ogni risposta imposta un cookie con un nome nuovo: e' il caso in cui la collezione cresce
                server.Handler = _ => ("HTTP/1.1 200 OK\r\nContent-Type: text/html\r\n" +
                                       $"Set-Cookie: c{Interlocked.Increment(ref n)}=v; path=/\r\n", Encoding.UTF8.GetBytes("ok"));

                using (var start = new ManualResetEventSlim(false)) {
                    var tasks = Enumerable.Range(0, 40).Select(_ => Task.Run(() => {
                        start.Wait();
                        PageLoader.getResponseMessage(server.BaseUrl)?.Dispose();
                        PageLoader.getCookie("c1");
                    })).ToArray();
                    start.Set();
                    Assert.That(() => Task.WaitAll(tasks), Throws.Nothing);
                }

                Assert.That(PageLoader.cookies.Count, Is.EqualTo(40));
                Assert.That(Enumerable.Range(1, 40).All(i => PageLoader.getCookie("c" + i) == "v"), Is.True);
            }
        }
    }
}
