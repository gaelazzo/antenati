using NUnit.Framework;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace viewer.Tests {

    /// <summary>
    /// Server HTTP/1.1 minimo su 127.0.0.1: una risposta per connessione, registra le richieste.
    /// </summary>
    sealed class LocalHttpServer : IDisposable {
        readonly TcpListener listener;
        readonly CancellationTokenSource cts = new CancellationTokenSource();
        public readonly ConcurrentQueue<string> Requests = new ConcurrentQueue<string>();
        public Func<string, (string head, byte[] body)> Handler;

        public string BaseUrl { get; }

        public LocalHttpServer() {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            BaseUrl = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
            Task.Run(Loop);
        }

        async Task Loop() {
            while (!cts.IsCancellationRequested) {
                TcpClient c;
                try { c = await listener.AcceptTcpClientAsync(); }
                catch { return; }
                using (c)
                using (var s = c.GetStream()) {
                    var sb = new StringBuilder();
                    var buf = new byte[1];
                    while (!sb.ToString().EndsWith("\r\n\r\n") && await s.ReadAsync(buf, 0, 1) == 1)
                        sb.Append((char)buf[0]);
                    string req = sb.ToString();
                    Requests.Enqueue(req);
                    var (head, body) = Handler(req);
                    byte[] h = Encoding.ASCII.GetBytes(head + $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                    await s.WriteAsync(h, 0, h.Length);
                    await s.WriteAsync(body, 0, body.Length);
                }
            }
        }

        public void Dispose() {
            cts.Cancel();
            listener.Stop();
        }
    }

    [TestFixture]
    public class PageLoaderTests {
        LocalHttpServer server;

        [SetUp]
        public void SetUp() {
            Tree.Reset();
            server = new LocalHttpServer();
        }

        [TearDown]
        public void TearDown() { server.Dispose(); }

        static (string, byte[]) Ok(string body, string extraHeaders = "") {
            return ("HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n" + extraHeaders, Encoding.UTF8.GetBytes(body));
        }

        [Test]
        public void GetPage_DecodificaUtf8() {
            server.Handler = _ => Ok("<h3>Località Città di Sant'Agata</h3>");
            Assert.That(PageLoader.getPage(server.BaseUrl), Is.EqualTo("<h3>Località Città di Sant'Agata</h3>"));
        }

        [Test]
        public void Richiesta_ConUserAgentBrowserEHeaderAggiuntivi() {
            server.Handler = _ => Ok("ok");
            var headers = new Dictionary<string, string> { ["Referer"] = "https://antenati.cultura.gov.it", ["Accept-Language"] = "it" };
            PageLoader.getResponseMessage(server.BaseUrl, headers: headers).Dispose();

            Assert.That(server.Requests.TryDequeue(out var req), Is.True);
            Assert.That(req, Does.Contain("User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64)"));
            Assert.That(req, Does.Contain("Referer: https://antenati.cultura.gov.it"));
            Assert.That(req, Does.Contain("Accept-Language: it"));
        }

        [Test]
        public void Richiesta_InviaICookiePassati() {
            server.Handler = _ => Ok("ok");
            var cookies = new Dictionary<string, string> { ["PHPSESSID"] = "abc123", ["vuoto"] = null };
            PageLoader.getResponseMessage(server.BaseUrl, cookiesDict: cookies).Dispose();

            server.Requests.TryDequeue(out var req);
            Assert.That(req, Does.Contain("PHPSESSID=abc123"));
        }

        [Test]
        public void SetCookie_MemorizzatoEPoiAggiornato() {
            string value = "uno";
            server.Handler = _ => Ok("ok", $"Set-Cookie: PHPSESSID={value}; path=/\r\n");

            PageLoader.getResponseMessage(server.BaseUrl).Dispose();
            Assert.That(PageLoader.getCookie("PHPSESSID"), Is.EqualTo("uno"));

            value = "due";
            PageLoader.getResponseMessage(server.BaseUrl).Dispose();
            Assert.That(PageLoader.getCookie("PHPSESSID"), Is.EqualTo("due"));
            Assert.That(PageLoader.cookies.Count, Is.EqualTo(1), "aggiornato, non duplicato");
        }

        [Test]
        public void GetCookie_Mancante_Null() {
            Assert.That(PageLoader.getCookie("nessuno"), Is.Null);
        }

        [Test]
        public void RispostaGzip_Decompressa() {
            var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, CompressionMode.Compress, true)) {
                var b = Encoding.UTF8.GetBytes("{\"width\":2000}");
                gz.Write(b, 0, b.Length);
            }
            server.Handler = _ => ("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Encoding: gzip\r\n", ms.ToArray());

            Assert.That(PageLoader.getPage(server.BaseUrl), Is.EqualTo("{\"width\":2000}"));
        }

        [Test]
        public void Redirect_Seguito_UriFinaleNellaRisposta() {
            // E' cosi' che i read() riconoscono il caso "un solo risultato": Antenati redirige alla pagina del registro
            server.Handler = req => req.StartsWith("GET /cerca")
                ? ($"HTTP/1.1 302 Found\r\nLocation: {server.BaseUrl}ark:/12657/an_ua282248/\r\n", new byte[0])
                : Ok("registro");

            using (var r = PageLoader.getResponseMessage(server.BaseUrl + "cerca?serie=1")) {
                Assert.That(r.RequestMessage.RequestUri.AbsoluteUri, Is.EqualTo(server.BaseUrl + "ark:/12657/an_ua282248/"));
                Assert.That(r.Content.ReadAsStringAsync().Result, Is.EqualTo("registro"));
            }
        }

        [Test]
        public void ServerIrraggiungibile_Null() {
            int port;
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            Assert.That(PageLoader.getResponseMessage($"http://127.0.0.1:{port}/"), Is.Null);
        }

        [Test]
        public void WaitWarmUp_SenzaWarmUp_NonBlocca() {
            Assert.That(() => PageLoader.WaitWarmUp(), Throws.Nothing);
        }
    }
}
