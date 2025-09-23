// File: Services/Retrieval/Streaming/LocalServer.cs
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NovaGM.Services.Multiplayer;
using QRCoder;

namespace NovaGM.Services.Streaming
{
    /// Minimal in-process ASP.NET Core server for join/play/SSE on the local network.
    public sealed class LocalServer : IDisposable
    {
        private readonly GameCoordinator _coordinator;
        private IHost? _host;
        private CancellationTokenSource? _shutdownCts;

        public int Port { get; private set; }
        public bool AllowLan { get; private set; }
        public string[] LanIps { get; private set; } = Array.Empty<string>();

        public LocalServer(GameCoordinator coordinator) => _coordinator = coordinator;

        public void Start(int port, bool allowLan)
        {
            Port = port;
            AllowLan = allowLan;
            _shutdownCts = new CancellationTokenSource();

            var url = allowLan ? $"http://0.0.0.0:{port}" : $"http://127.0.0.1:{port}";
            LanIps = allowLan ? GetLanIPv4() : Array.Empty<string>();

            _host = Host.CreateDefaultBuilder()
                .ConfigureWebHostDefaults(web =>
                {
                    web.UseKestrel()
                       .UseUrls(url)
                       .ConfigureServices(s => s.AddRouting())
                       .Configure(app =>
                       {
                           app.UseRouting();
                           app.UseEndpoints(endpoints =>
                           {
                               // GET /
                               endpoints.MapGet("/", async ctx =>
                               {
                                   var code = _coordinator.CurrentCode ?? "";
                                   var codeHtml = HtmlEncoder.Default.Encode(code);

                                   var lanList = LanIps.Length == 0
                                       ? "<li>(no LAN IPv4 detected)</li>"
                                       : string.Join("", LanIps.Select(ip =>
                                           $"<li><a href=\"http://{ip}:{Port}/\">http://{ip}:{Port}/</a></li>"));

                                   var html = BuildJoinPage(codeHtml, lanList);
                                   ctx.Response.ContentType = "text/html; charset=utf-8";
                                   await ctx.Response.WriteAsync(html);
                               });

                               // GET /play
                               endpoints.MapGet("/play", async ctx =>
                               {
                                   var name = ctx.Request.Query["name"].ToString();
                                   var code = ctx.Request.Query["code"].ToString();

                                   var nameHtml = HtmlEncoder.Default.Encode(name);
                                   var codeJs   = JavaScriptEncoder.Default.Encode(code);
                                   var nameJs   = JavaScriptEncoder.Default.Encode(name);

                                   var html = BuildPlayPage(nameHtml, codeJs, nameJs);
                                   ctx.Response.ContentType = "text/html; charset=utf-8";
                                   await ctx.Response.WriteAsync(html);
                               });

                               // GET /stream  (SSE)
                               endpoints.MapGet("/stream", async ctx =>
                               {
                                   ctx.Response.Headers.Append("Cache-Control", "no-cache");
                                   ctx.Response.Headers.Append("Content-Type", "text/event-stream");
                                   ctx.Response.Headers.Append("X-Accel-Buffering", "no");

                                   var linked = CancellationTokenSource.CreateLinkedTokenSource(
                                       ctx.RequestAborted,
                                       _shutdownCts!.Token
                                   );
                                   var token = linked.Token;

                                   try
                                   {
                                       await foreach (var chunk in LocalBroadcaster.Instance.Subscribe(token))
                                       {
                                           await ctx.Response.WriteAsync($"data: {chunk}\n\n", token);
                                           await ctx.Response.Body.FlushAsync(token);
                                       }
                                   }
                                   catch (OperationCanceledException)
                                   {
                                       // expected on shutdown / disconnect
                                   }
                                   finally
                                   {
                                       linked.Dispose();
                                   }
                               });

                               // POST /input  { code, name, text }
                               endpoints.MapPost("/input", async ctx =>
                               {
                                   using var sr = new StreamReader(ctx.Request.Body);
                                   var body = await sr.ReadToEndAsync();
                                   try
                                   {
                                       using var doc  = JsonDocument.Parse(body);
                                       var root       = doc.RootElement;
                                       var code       = root.GetProperty("code").GetString() ?? "";
                                       var name       = root.GetProperty("name").GetString() ?? "Player";
                                       var text       = root.GetProperty("text").GetString() ?? "";

                                       if (string.IsNullOrWhiteSpace(text))
                                       {
                                           ctx.Response.StatusCode = 400;
                                           await ctx.Response.WriteAsync("missing text");
                                           return;
                                       }
                                       if (!_coordinator.TryEnqueue(code, name, text))
                                       {
                                           ctx.Response.StatusCode = 403;
                                           await ctx.Response.WriteAsync("bad room code");
                                           return;
                                       }
                                       ctx.Response.StatusCode = 204;
                                   }
                                   catch
                                   {
                                       ctx.Response.StatusCode = 400;
                                       await ctx.Response.WriteAsync("bad json");
                                   }
                               });

                               // GET /qr?code=ABCD
                               endpoints.MapGet("/qr", async ctx =>
                               {
                                   var code = ctx.Request.Query["code"].ToString();
                                   var joinUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}/?code={WebUtility.UrlEncode(code)}";

                                   using var qrGen = new QRCodeGenerator();
                                   using var data  = qrGen.CreateQrCode(joinUrl, QRCodeGenerator.ECCLevel.Q);
                                   var svg = new SvgQRCode(data).GetGraphic(6);
                                   ctx.Response.ContentType = "image/svg+xml";
                                   await ctx.Response.WriteAsync(svg);
                               });

                               // GET /health
                               endpoints.MapGet("/health", async ctx => { await ctx.Response.WriteAsync("ok"); });
                           });
                       });
                }).Build();

            _host.Start();

            Console.WriteLine($"[NovaGM] Web UI bound to: {(allowLan ? $"0.0.0.0:{port}" : $"127.0.0.1:{port}")}");
            if (allowLan && LanIps.Length > 0)
            {
                Console.WriteLine("         LAN URLs:");
                foreach (var ip in LanIps)
                    Console.WriteLine($"           http://{ip}:{port}  (join page at /)");
            }
            else if (allowLan)
            {
                Console.WriteLine("         No LAN IPv4 detected. Is your adapter up?");
            }
        }

        public void Dispose()
        {
            try { _shutdownCts?.Cancel(); } catch { }

            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                _host?.StopAsync(timeout.Token).GetAwaiter().GetResult();
            }
            catch { /* swallow */ }

            try { _host?.Dispose(); } catch { }
            try { _shutdownCts?.Dispose(); } catch { }

            _host = null;
            _shutdownCts = null;
        }

        private static string[] GetLanIPv4()
        {
            try
            {
                return Dns.GetHostEntry(Dns.GetHostName())
                    .AddressList
                    .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                    .Select(a => a.ToString())
                    .Distinct()
                    .ToArray();
            }
            catch { return Array.Empty<string>(); }
        }

        // ---------- HTML builders (no raw-string literals) ----------

        private static string BuildJoinPage(string codeHtml, string lanListHtml)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!doctype html>");
            sb.AppendLine("<html>");
            sb.AppendLine("<head>");
            sb.AppendLine("  <meta charset=\"utf-8\" />");
            sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
            sb.AppendLine("  <title>NovaGM — Join</title>");
            sb.AppendLine("  <style>");
            sb.AppendLine("    body{font-family:system-ui,-apple-system,Segoe UI,Roboto,Ubuntu,sans-serif;margin:2rem;line-height:1.35}");
            sb.AppendLine("    input,button{font-size:1rem;padding:.5rem .75rem;border-radius:.4rem;border:1px solid #ccc}");
            sb.AppendLine("    form{display:flex;gap:.5rem;flex-wrap:wrap}");
            sb.AppendLine("    .row{display:flex;align-items:flex-start;gap:1rem;margin-top:1rem;}");
            sb.AppendLine("    .qr{border:1px solid #ddd;border-radius:.5rem;padding:.5rem;background:#fff}");
            sb.AppendLine("  </style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine("  <h2>Join NovaGM</h2>");
            sb.Append("  <p>Room code: <b>").Append(codeHtml).AppendLine("</b></p>");
            sb.AppendLine("  <form action=\"/play\" method=\"get\">");
            sb.AppendLine("    <label>Your name:");
            sb.AppendLine("      <input name=\"name\" required placeholder=\"Player name\"/>");
            sb.AppendLine("    </label>");
            sb.Append("    <input type=\"hidden\" name=\"code\" value=\"").Append(codeHtml).AppendLine("\" />");
            sb.AppendLine("    <button type=\"submit\">Join</button>");
            sb.AppendLine("  </form>");
            sb.AppendLine("  <div class=\"row\">");
            sb.AppendLine("    <div>");
            sb.AppendLine("      <p>Or scan this on your phone:</p>");
            sb.Append("      <img class=\"qr\" alt=\"QR\" src=\"/qr?code=").Append(codeHtml).AppendLine("\" />");
            sb.AppendLine("    </div>");
            sb.AppendLine("    <div>");
            sb.AppendLine("      <p>LAN URLs (same network):</p>");
            sb.AppendLine("      <ul>");
            sb.AppendLine(lanListHtml);
            sb.AppendLine("      </ul>");
            sb.AppendLine("    </div>");
            sb.AppendLine("  </div>");
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");
            return sb.ToString();
        }

        private static string BuildPlayPage(string nameHtml, string codeJs, string nameJs)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!doctype html>");
            sb.AppendLine("<html>");
            sb.AppendLine("<head>");
            sb.AppendLine("  <meta charset=\"utf-8\" />");
            sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
            sb.Append("  <title>NovaGM — ").Append(nameHtml).AppendLine("</title>");
            sb.AppendLine("  <style>");
            sb.AppendLine("    body{font-family:system-ui,-apple-system,Segoe UI,Roboto,Ubuntu,sans-serif;margin:1rem}");
            sb.AppendLine("    #log{white-space:pre-wrap;border:1px solid #ddd;padding:10px;height:60vh;overflow:auto;border-radius:6px;background:#fff}");
            sb.AppendLine("    #row{margin-top:8px;display:flex;gap:8px}");
            sb.AppendLine("    textarea{flex:1;min-height:3em}");
            sb.AppendLine("    button{padding:.5rem .75rem;border-radius:.4rem;border:1px solid #ccc}");
            sb.AppendLine("  </style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.Append("  <h3>NovaGM — ").Append(nameHtml).AppendLine("</h3>");
            sb.AppendLine("  <div id=\"log\"></div>");
            sb.AppendLine("  <div id=\"row\">");
            sb.AppendLine("    <textarea id=\"msg\" placeholder=\"Your action...\"></textarea>");
            sb.AppendLine("    <button id=\"send\">Send</button>");
            sb.AppendLine("  </div>");
            sb.AppendLine("  <script>");
            sb.AppendLine("  const log  = document.getElementById('log');");
            sb.AppendLine("  const msg  = document.getElementById('msg');");
            sb.AppendLine("  const send = document.getElementById('send');");
            sb.AppendLine("  const es = new EventSource('/stream');");
            sb.AppendLine("  es.onmessage = (e) => {");
            sb.AppendLine("    log.textContent += e.data;");
            sb.AppendLine("    log.scrollTop = log.scrollHeight;");
            sb.AppendLine("  };");
            sb.AppendLine("  async function submit() {");
            sb.AppendLine("    const text = msg.value.trim();");
            sb.AppendLine("    if (!text) return;");
            sb.AppendLine("    msg.value = '';");
            sb.Append("    await fetch('/input', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ code: '")
              .Append(codeJs).Append("', name: '").Append(nameJs).AppendLine("', text }) });");
            sb.AppendLine("  }");
            sb.AppendLine("  send.onclick = submit;");
            sb.AppendLine("  msg.addEventListener('keydown', (ev) => { if (ev.key === 'Enter' && !ev.shiftKey) { ev.preventDefault(); submit(); } });");
            sb.AppendLine("  </script>");
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");
            return sb.ToString();
        }
    }
}
