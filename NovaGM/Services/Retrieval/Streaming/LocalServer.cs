// NovaGM/Services/Retrieval/Streaming/LocalServer.cs
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NovaGM.Services;
using NovaGM.Services.Multiplayer;

namespace NovaGM.Services.Streaming
{
    /// Minimal in-process ASP.NET Core server for join/play/SSE and player HUD.
    public sealed class LocalServer : IDisposable
    {
        private readonly GameCoordinator _coordinator;
        private IHost? _host;
        private CancellationTokenSource? _shutdownCts;

        public int Port { get; private set; }
        public bool AllowLan { get; private set; }
        public string[] LanIps { get; private set; } = Array.Empty<string>();
        public string JoinUrl => AllowLan && LanIps.Length > 0 
            ? $"http://{LanIps[0]}:{Port}" 
            : $"http://127.0.0.1:{Port}";

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
                       .ConfigureServices(services => services.AddRouting())
                       .Configure(app =>
                       {
                           app.UseRouting();

                           app.UseEndpoints(endpoints =>
                           {
                               // GET /
                               endpoints.MapGet("/", async ctx =>
                               {
                                   ctx.Response.ContentType = "text/html; charset=utf-8";
                                   var code = _coordinator.CurrentCode;
                                   var qrDataUrl = "";
                                   try
                                   {
                                       var joinUrl = AllowLan && LanIps.Length > 0 
                                           ? $"http://{LanIps[0]}:{Port}?code={code}" 
                                           : $"http://127.0.0.1:{Port}?code={code}";
                                       qrDataUrl = QRCodeService.GenerateQRCodeDataUrl(joinUrl);
                                   }
                                   catch { /* QR generation failed, continue without */ }

                                   await ctx.Response.WriteAsync($@"<!doctype html>
<html><head><meta charset='utf-8'><title>NovaGM — Join</title>
<style>body{{font-family:sans-serif;margin:2rem;}}input,button{{font-size:1rem;}}</style></head>
<body>
  <h2>Join NovaGM</h2>
  <p>Room code: <b>{WebUtility.HtmlEncode(code)}</b></p>
  {(string.IsNullOrEmpty(qrDataUrl) ? "" : $"<p><img src='{qrDataUrl}' alt='QR Code' style='border:1px solid #ddd;'/></p>")}
  <form action='/hud' method='get'>
    <label>Your name: <input name='name' required></label>
    <input type='hidden' name='code' value='{WebUtility.HtmlEncode(code)}'>
    <button type='submit'>Continue</button>
  </form>
  <p style='margin-top:1rem;color:#666'>Already joined? <a href='/play?name=Player&code={WebUtility.HtmlEncode(code)}'>Go to chat view</a></p>
</body></html>");
                               });

                               // GET /play  (simple chat view)
                               endpoints.MapGet("/play", async ctx =>
                               {
                                   var name = ctx.Request.Query["name"].ToString();
                                   var code = ctx.Request.Query["code"].ToString();
                                   var nameJs = JavaScriptEncoder.Default.Encode(name);
                                   var codeJs = JavaScriptEncoder.Default.Encode(code);

                                   ctx.Response.ContentType = "text/html; charset=utf-8";
                                   await ctx.Response.WriteAsync($@"<!doctype html>
<html><head><meta charset='utf-8'><title>NovaGM — {WebUtility.HtmlEncode(name)}</title>
<style>
body{{font-family:sans-serif;margin:1rem;}}
#log{{white-space:pre-wrap;border:1px solid #ddd;padding:10px;height:60vh;overflow:auto;border-radius:6px;}}
#row{{margin-top:8px;display:flex;gap:8px;}}
textarea{{flex:1;min-height:3em;}}
</style></head>
<body>
  <h3>NovaGM — {WebUtility.HtmlEncode(name)}</h3>
  <div id='log'></div>
  <div id='row'>
    <textarea id='msg' placeholder='Your action...'></textarea>
    <button id='send'>Send</button>
  </div>
<script>
const log = document.getElementById('log');
const msg = document.getElementById('msg');
const send = document.getElementById('send');
const es = new EventSource('/stream');
es.onmessage = (e) => {{ log.textContent += e.data; log.scrollTop = log.scrollHeight; }};
send.onclick = async () => {{
  const text = msg.value.trim();
  if(!text) return;
  msg.value = '';
  await fetch('/input', {{
    method: 'POST',
    headers: {{ 'Content-Type': 'application/json' }},
    body: JSON.stringify({{ code: '{codeJs}', name: '{nameJs}', text }})
  }});
}};
</script>
</body></html>");
                               });

                               // GET /hud  (player HUD with Character tab + Action)
                               endpoints.MapGet("/hud", async ctx =>
                               {
                                   var name = ctx.Request.Query["name"].ToString();
                                   var code = ctx.Request.Query["code"].ToString();
                                   var nameJs = JavaScriptEncoder.Default.Encode(name);
                                   var codeJs = JavaScriptEncoder.Default.Encode(code);

                                   ctx.Response.ContentType = "text/html; charset=utf-8";
                                   await ctx.Response.WriteAsync($@"<!doctype html>
<html><head><meta charset='utf-8'><title>NovaGM — HUD ({WebUtility.HtmlEncode(name)})</title>
<meta name=viewport content='width=device-width, initial-scale=1'/>
<style>
body{{font-family:sans-serif;margin:12px;}}
.nav{{display:flex;gap:12px;margin-bottom:12px;}}
.nav a{{text-decoration:none;padding:6px 10px;border:1px solid #ccc;border-radius:6px;}}
.card{{border:1px solid #ddd;border-radius:8px;padding:10px;margin-bottom:12px;}}
.row{{display:flex;gap:8px;flex-wrap:wrap}}
.row>label{{display:flex;flex-direction:column;min-width:120px;}}
input[type=number]{{width:70px}}
button{{padding:6px 10px}}
.auto-gen{{background:#f0f8ff;border:1px solid #4a90e2;border-radius:6px;padding:8px;margin-bottom:8px;}}
.auto-gen-buttons{{display:flex;gap:6px;flex-wrap:wrap;}}
.auto-gen button{{background:#4a90e2;color:white;border:none;border-radius:4px;padding:4px 8px;font-size:12px;}}
</style></head>
<body>
  <div class='nav'>
    <a href='#char'>Character</a>
    <a href='#act'>Action</a>
    <a href='/play?name={WebUtility.HtmlEncode(name)}&code={WebUtility.HtmlEncode(code)}'>Text View</a>
  </div>

  <div id='char' class='card'>
    <h3>Character</h3>
    
    <div class='auto-gen'>
      <div style='font-weight:bold;margin-bottom:4px;'>Quick Generation:</div>
      <div class='auto-gen-buttons'>
        <button onclick='generateCharacter(""random"")'>Random</button>
        <button onclick='generateCharacter(""fighter"")'>Fighter</button>
        <button onclick='generateCharacter(""rogue"")'>Rogue</button>
        <button onclick='generateCharacter(""mage"")'>Mage</button>
      </div>
    </div>
    
    <div class='row'>
      <label>Name<input id='pc_name'/></label>
      <label>Race<input id='pc_race'/></label>
      <label>Class<input id='pc_class'/></label>
    </div>
    <div class='row' style='margin-top:6px'>
      <label>STR<input id='pc_str' type='number'/></label>
      <label>DEX<input id='pc_dex' type='number'/></label>
      <label>CON<input id='pc_con' type='number'/></label>
      <label>INT<input id='pc_int' type='number'/></label>
      <label>WIS<input id='pc_wis' type='number'/></label>
      <label>CHA<input id='pc_cha' type='number'/></label>
    </div>
    <div style='margin-top:8px'><button id='save'>Save</button></div>
  </div>

  <div id='act' class='card'>
    <h3>Action</h3>
    <textarea id='msg' style='width:100%;min-height:5em' placeholder='Your action...'></textarea>
    <div style='margin-top:6px'><button id='send'>Send</button></div>
  </div>

<script>
const nameV = '{nameJs}', codeV = '{codeJs}';

// Auto-generation function
async function generateCharacter(type) {{
  try {{
    const response = await fetch('/generate-character', {{
      method: 'POST',
      headers: {{ 'Content-Type': 'application/json' }},
      body: JSON.stringify({{ type: type }})
    }});
    
    if (response.ok) {{
      const character = await response.json();
      
      // Fill form with generated character
      document.getElementById('pc_name').value = character.name || '';
      document.getElementById('pc_race').value = character.race || '';
      document.getElementById('pc_class').value = character.class || '';
      document.getElementById('pc_str').value = character.stats?.str || 10;
      document.getElementById('pc_dex').value = character.stats?.dex || 10;
      document.getElementById('pc_con').value = character.stats?.con || 10;
      document.getElementById('pc_int').value = character.stats?.int || 10;
      document.getElementById('pc_wis').value = character.stats?.wis || 10;
      document.getElementById('pc_cha').value = character.stats?.cha || 10;
    }}
  }} catch (e) {{
    console.error('Failed to generate character:', e);
    alert('Failed to generate character. Please try again.');
  }}
}}

// load existing pc
(async () => {{
  const r = await fetch(`/character?name=${{encodeURIComponent(nameV)}}&code=${{encodeURIComponent(codeV)}}`);
  if(r.ok) {{
    const pc = await r.json();
    if(pc) {{
      for (const [k,v] of Object.entries(pc)) {{
        const el = document.getElementById('pc_' + k.toLowerCase());
        if(el) el.value = v ?? '';
      }}
    }}
  }}
}})();

document.getElementById('save').onclick = async () => {{
  const pc = {{
    Name:  document.getElementById('pc_name').value,
    Race:  document.getElementById('pc_race').value,
    Class: document.getElementById('pc_class').value,
    STR: +document.getElementById('pc_str').value||0,
    DEX: +document.getElementById('pc_dex').value||0,
    CON: +document.getElementById('pc_con').value||0,
    INT: +document.getElementById('pc_int').value||0,
    WIS: +document.getElementById('pc_wis').value||0,
    CHA: +document.getElementById('pc_cha').value||0
  }};
  await fetch('/character', {{
    method:'POST',
    headers: {{'Content-Type':'application/json'}},
    body: JSON.stringify({{ code: codeV, name: nameV, pc }})
  }});
  alert('Saved!');
}};

document.getElementById('send').onclick = async () => {{
  const box = document.getElementById('msg');
  const text = box.value.trim();
  if(!text) return;
  box.value = '';
  await fetch('/input', {{
    method:'POST',
    headers: {{'Content-Type':'application/json'}},
    body: JSON.stringify({{ code: codeV, name: nameV, text }})
  }});
}};
</script>
</body></html>");
                               });

                               // GET /character?name=&code=
                               endpoints.MapGet("/character", async ctx =>
                               {
                                   var name = ctx.Request.Query["name"].ToString();
                                   var code = ctx.Request.Query["code"].ToString();
                                   ctx.Response.ContentType = "application/json";
                                   if (_coordinator.TryGetCharacter(code, name, out var pc))
                                   {
                                       await ctx.Response.WriteAsync(JsonSerializer.Serialize(pc));
                                   }
                                   else
                                   {
                                       await ctx.Response.WriteAsync("null");
                                   }
                               });

                               // POST /character { code, name, pc:{} }
                               endpoints.MapPost("/character", async ctx =>
                               {
                                   using var sr = new StreamReader(ctx.Request.Body);
                                   var body = await sr.ReadToEndAsync();
                                   try
                                   {
                                       using var doc = JsonDocument.Parse(body);
                                       var root = doc.RootElement;
                                       var code = root.GetProperty("code").GetString() ?? "";
                                       var name = root.GetProperty("name").GetString() ?? "";
                                       var pc   = root.GetProperty("pc");

                                       var model = new PlayerCharacter
                                       {
                                           Name  = pc.GetProperty("Name").GetString() ?? "",
                                           Race  = pc.GetProperty("Race").GetString() ?? "",
                                           Class = pc.GetProperty("Class").GetString() ?? "",
                                           STR = pc.TryGetProperty("STR", out var str) ? str.GetInt32() : 0,
                                           DEX = pc.TryGetProperty("DEX", out var dex) ? dex.GetInt32() : 0,
                                           CON = pc.TryGetProperty("CON", out var con) ? con.GetInt32() : 0,
                                           INT = pc.TryGetProperty("INT", out var intel) ? intel.GetInt32() : 0,
                                           WIS = pc.TryGetProperty("WIS", out var wis) ? wis.GetInt32() : 0,
                                           CHA = pc.TryGetProperty("CHA", out var cha) ? cha.GetInt32() : 0,
                                       };

                                       _coordinator.SetCharacter(code, name, model);
                                       ctx.Response.StatusCode = 204;
                                   }
                                   catch
                                   {
                                       ctx.Response.StatusCode = 400;
                                       await ctx.Response.WriteAsync("bad json");
                                   }
                               });

                               // GET /stream  (SSE) — linked to app shutdown + this server shutdown
                               endpoints.MapGet("/stream", async ctx =>
                               {
                                   ctx.Response.Headers.Append("Cache-Control", "no-cache");
                                   ctx.Response.Headers.Append("Content-Type", "text/event-stream");
                                   ctx.Response.Headers.Append("X-Accel-Buffering", "no");

                                   using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                                       ctx.RequestAborted,
                                       _shutdownCts!.Token,
                                       ShutdownUtil.Token
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
                                       // expected on shutdown
                                   }
                               });

                               // POST /input  { code, name, text }
                               endpoints.MapPost("/input", async ctx =>
                               {
                                   using var sr = new StreamReader(ctx.Request.Body);
                                   var body = await sr.ReadToEndAsync();
                                   try
                                   {
                                       using var doc = JsonDocument.Parse(body);
                                       var root = doc.RootElement;
                                       var code = root.GetProperty("code").GetString() ?? "";
                                       var name = root.GetProperty("name").GetString() ?? "Player";
                                       var text = root.GetProperty("text").GetString() ?? "";

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

                               // POST /generate-character { type }
                               endpoints.MapPost("/generate-character", async ctx =>
                               {
                                   using var sr = new StreamReader(ctx.Request.Body);
                                   var body = await sr.ReadToEndAsync();
                                   try
                                   {
                                       using var doc = JsonDocument.Parse(body);
                                       var type = doc.RootElement.GetProperty("type").GetString() ?? "random";

                                       GeneratedCharacter character = type.ToLowerInvariant() switch
                                       {
                                           "fighter" => CharacterGenerator.GenerateForClass("fighter"),
                                           "rogue" => CharacterGenerator.GenerateForClass("rogue"),
                                           "mage" => CharacterGenerator.GenerateForClass("mage"),
                                           _ => CharacterGenerator.GenerateRandom()
                                       };

                                       var response = new
                                       {
                                           name = character.Name,
                                           race = character.Race,
                                           @class = character.Class,
                                           stats = new
                                           {
                                               str = character.GetBaseStat("str"),
                                               dex = character.GetBaseStat("dex"),
                                               con = character.GetBaseStat("con"),
                                               @int = character.GetBaseStat("int"),
                                               wis = character.GetBaseStat("wis"),
                                               cha = character.GetBaseStat("cha")
                                           }
                                       };

                                       ctx.Response.ContentType = "application/json";
                                       await ctx.Response.WriteAsync(JsonSerializer.Serialize(response));
                                   }
                                   catch
                                   {
                                       ctx.Response.StatusCode = 400;
                                       await ctx.Response.WriteAsync("bad request");
                                   }
                               });

                               // POST /dice  { expression }
                               endpoints.MapPost("/dice", async ctx =>
                               {
                                   using var sr = new StreamReader(ctx.Request.Body);
                                   var body = await sr.ReadToEndAsync();
                                   try
                                   {
                                       using var doc = JsonDocument.Parse(body);
                                       var expr = doc.RootElement.GetProperty("expression").GetString() ?? "1d20";
                                       var result = DiceService.Roll(expr);
                                       
                                       ctx.Response.ContentType = "application/json";
                                       await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
                                       {
                                           expression = result.Expression,
                                           rolls = result.Rolls,
                                           total = result.Total,
                                           description = result.Description
                                       }));
                                   }
                                   catch
                                   {
                                       ctx.Response.StatusCode = 400;
                                       await ctx.Response.WriteAsync("bad request");
                                   }
                               });

                               // GET /health
                               endpoints.MapGet("/health", async ctx =>
                               {
                                   await ctx.Response.WriteAsync("ok");
                               });
                           });
                       });
                }).Build();

            _host.Start();

            // Let app-wide shutdown stop this host, too.
            ShutdownUtil.RegisterAsyncDisposer(async () => await StopAsync());

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

        public async Task StopAsync()
        {
            try { _shutdownCts?.Cancel(); } catch { }
            if (_host != null)
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await _host.StopAsync(timeout.Token).ConfigureAwait(false);
                }
                catch { /* swallow */ }
                try { _host.Dispose(); } catch { }
                _host = null;
            }
            try { _shutdownCts?.Dispose(); } catch { }
            _shutdownCts = null;
        }

        public void Dispose()
        {
            try { StopAsync().GetAwaiter().GetResult(); } catch { }
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
    }
}
