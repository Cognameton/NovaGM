// NovaGM/Services/Retrieval/Streaming/LocalServer.cs
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

// Load conversation history first
async function loadHistory() {{
  try {{
    const response = await fetch('/history');
    if (response.ok) {{
      const history = await response.json();
      // Clear any existing content
      log.textContent = '';
      // Add each message to the log
      history.forEach(message => {{
        if (message.content && message.content.trim()) {{
          log.textContent += `${{message.role}}: ${{message.content}}\\n`;
        }}
      }});
      // Scroll to bottom
      log.scrollTop = log.scrollHeight;
    }}
  }} catch (e) {{
    console.error('Failed to load history:', e);
    // Continue with just the stream if history fails
  }}
}}

// Load history when page loads
loadHistory();

// Then start streaming new messages
const es = new EventSource('/stream');
es.onmessage = (e) => {{ 
  log.textContent += e.data; 
  log.scrollTop = log.scrollHeight; 
}};

// Handle connection errors and reconnection
es.onerror = () => {{
  console.log('Stream disconnected, will reconnect automatically');
}};

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

// Add enter key support for sending messages
msg.addEventListener('keypress', (e) => {{
  if (e.key === 'Enter' && !e.shiftKey) {{
    e.preventDefault();
    send.click();
  }}
}});
</script>
</body></html>");
                               });

                               // GET /hud  (player HUD with Character tab + Action)
                               endpoints.MapGet("/hud", async ctx =>
                               {
                                   var name = ctx.Request.Query["name"].ToString();
                                   var code = ctx.Request.Query["code"].ToString();

                                   ctx.Response.ContentType = "text/html; charset=utf-8";
                                   var nameLiteral = JsonSerializer.Serialize(name ?? string.Empty);
                                   var codeLiteral = JsonSerializer.Serialize(code ?? string.Empty);

                                   var html = new StringBuilder();
                                   html.AppendLine("<!doctype html>");
                                   html.AppendLine("<html><head><meta charset='utf-8'><title>NovaGM — HUD (" + WebUtility.HtmlEncode(name) + ")</title>");
                                   html.AppendLine("<meta name=viewport content='width=device-width, initial-scale=1'/>");
                                   html.AppendLine("<style>");
                                   html.AppendLine("body{font-family:sans-serif;margin:12px;background:#f7f9fb;}");
                                   html.AppendLine(".nav{display:flex;gap:8px;margin-bottom:12px;flex-wrap:wrap;}");
                                   html.AppendLine(".tab-btn{background:#eef1f7;border:1px solid #c8d0e0;border-radius:8px;padding:6px 14px;cursor:pointer;font-size:14px;color:#274064;}");
                                   html.AppendLine(".tab-btn.active{background:#4a90e2;color:#fff;border-color:#4a90e2;}");
                                   html.AppendLine(".view{display:none;}");
                                   html.AppendLine(".view.active{display:block;}");
                                   html.AppendLine(".card{background:#fff;border:1px solid #d9e2ef;border-radius:10px;padding:12px;margin-bottom:14px;box-shadow:0 1px 3px rgba(0,0,0,0.05);}");
                                   html.AppendLine(".row{display:flex;gap:8px;flex-wrap:wrap;}");
                                   html.AppendLine(".row>label{display:flex;flex-direction:column;min-width:140px;font-size:13px;color:#274064;gap:4px;}");
                                   html.AppendLine(".row input{padding:6px;border:1px solid #c8d0e0;border-radius:6px;}");
                                   html.AppendLine("textarea{width:100%;min-height:5em;padding:8px;border:1px solid #c8d0e0;border-radius:6px;font-size:14px;font-family:inherit;}");
                                   html.AppendLine("button{padding:6px 12px;border-radius:6px;border:1px solid #4a90e2;background:#4a90e2;color:#fff;cursor:pointer;font-size:14px;}");
                                   html.AppendLine("button.secondary{background:#eef1f7;color:#274064;border-color:#c8d0e0;}");
                                   html.AppendLine("button.small{padding:4px 10px;font-size:12px;}");
                                   html.AppendLine(".auto-gen{background:#eef6ff;border:1px solid #c6dcff;border-radius:8px;padding:8px;margin-bottom:10px;}");
                                   html.AppendLine(".auto-gen-buttons{display:flex;gap:6px;flex-wrap:wrap;}");
                                   html.AppendLine(".auto-gen button{background:#4a90e2;color:#fff;border:none;border-radius:6px;padding:4px 10px;font-size:12px;}");
                                   html.AppendLine(".auto-gen button:hover{opacity:0.9;}");
                                   html.AppendLine(".sheet-header{display:flex;justify-content:space-between;align-items:center;gap:12px;margin-bottom:10px;flex-wrap:wrap;}");
                                   html.AppendLine(".sheet-title{font-size:20px;font-weight:600;color:#1f2d3d;}");
                                   html.AppendLine(".sheet-meta{color:#4a5d75;font-size:13px;}");
                                   html.AppendLine(".sheet-stats{display:grid;grid-template-columns:repeat(auto-fit,minmax(80px,1fr));gap:8px;margin-top:10px;}");
                                   html.AppendLine(".sheet-stats div{background:#f0f4fb;border:1px solid #d9e2ef;border-radius:8px;padding:6px;text-align:center;}");
                                   html.AppendLine(".sheet-stats span{display:block;font-size:11px;color:#4a5d75;}");
                                   html.AppendLine(".sheet-stats strong{font-size:18px;color:#1f2d3d;}");
                                   html.AppendLine(".inventory-section{margin-top:12px;}");
                                   html.AppendLine(".inventory-title{font-weight:600;font-size:13px;margin-bottom:6px;color:#1f2d3d;}");
                                   html.AppendLine(".inventory-grid{display:grid;grid-template-columns:repeat(7,1fr);gap:4px;}");
                                   html.AppendLine(".inventory-cell{border:1px solid #d9e2ef;border-radius:6px;background:#f7f9fb;min-height:56px;display:flex;flex-direction:column;align-items:center;justify-content:center;padding:4px;font-size:11px;color:#1f2d3d;text-align:center;}");
                                   html.AppendLine(".inventory-cell.empty{opacity:0.45;}");
                                   html.AppendLine(".inventory-cell .qty{font-size:10px;color:#4a5d75;margin-top:2px;}");
                                   html.AppendLine(".muted{color:#60708c;font-size:12px;margin-top:12px;}");
                                   html.AppendLine("#char-status{margin-top:8px;font-size:13px;min-height:1em;}");
                                   html.AppendLine(".log{white-space:pre-wrap;border:1px solid #d9e2ef;padding:10px;height:55vh;overflow:auto;border-radius:10px;background:#fff;}");
                                   html.AppendLine("</style></head>");
                                   html.AppendLine("<body>");
                                   html.AppendLine("  <div class='nav'>");
                                   html.AppendLine("    <button class='tab-btn active' data-target='character'>Character</button>");
                                   html.AppendLine("    <button class='tab-btn' data-target='action'>Action</button>");
                                   html.AppendLine("    <button class='tab-btn' data-target='text'>Text View</button>");
                                   html.AppendLine("  </div>");
                                   html.AppendLine("  <section id='view-character' class='view active'>");
                                   html.AppendLine("    <div id='char-create' class='card'>");
                                   html.AppendLine("      <h3>Create or update your character</h3>");
                                   html.AppendLine("      <div class='auto-gen'>");
                                   html.AppendLine("        <div style='font-weight:bold;margin-bottom:4px;'>Quick Generation:</div>");
                                   html.AppendLine("        <div class='auto-gen-buttons'>");
                                   html.AppendLine("          <button onclick='generateCharacter(\"random\")'>Random</button>");
                                   html.AppendLine("          <button onclick='generateCharacter(\"fighter\")'>Fighter</button>");
                                   html.AppendLine("          <button onclick='generateCharacter(\"rogue\")'>Rogue</button>");
                                   html.AppendLine("          <button onclick='generateCharacter(\"mage\")'>Mage</button>");
                                   html.AppendLine("        </div>");
                                   html.AppendLine("      </div>");
                                   html.AppendLine("      <div class='row'>");
                                   html.AppendLine("        <label>Name<input id='pc_name' autocomplete='off'/></label>");
                                   html.AppendLine("        <label>Race<input id='pc_race' autocomplete='off'/></label>");
                                   html.AppendLine("        <label>Class<input id='pc_class' autocomplete='off'/></label>");
                                   html.AppendLine("        <label>Level<input id='pc_level' type='number' min='1' value='1'/></label>");
                                   html.AppendLine("      </div>");
                                   html.AppendLine("      <div class='row' style='margin-top:6px'>");
                                   html.AppendLine("        <label>STR<input id='pc_str' type='number'/></label>");
                                   html.AppendLine("        <label>DEX<input id='pc_dex' type='number'/></label>");
                                   html.AppendLine("        <label>CON<input id='pc_con' type='number'/></label>");
                                   html.AppendLine("        <label>INT<input id='pc_int' type='number'/></label>");
                                   html.AppendLine("        <label>WIS<input id='pc_wis' type='number'/></label>");
                                   html.AppendLine("        <label>CHA<input id='pc_cha' type='number'/></label>");
                                   html.AppendLine("      </div>");
                                   html.AppendLine("      <div style='margin-top:10px;display:flex;gap:8px;flex-wrap:wrap;'>");
                                   html.AppendLine("        <button id='save'>Save Character</button>");
                                   html.AppendLine("      </div>");
                                   html.AppendLine("      <div id='char-status'></div>");
                                   html.AppendLine("    </div>");
                                   html.AppendLine("    <div id='char-sheet' class='card' style='display:none;'>");
                                   html.AppendLine("      <div class='sheet-header'>");
                                   html.AppendLine("        <div>");
                                   html.AppendLine("          <div class='sheet-title' id='sheet-header'>Unnamed Adventurer</div>");
                                   html.AppendLine("          <div class='sheet-meta' id='sheet-meta'>Awaiting details</div>");
                                   html.AppendLine("        </div>");
                                   html.AppendLine("        <div style='display:flex;gap:6px;'>");
                                   html.AppendLine("          <button id='refresh-char' class='small secondary'>Refresh</button>");
                                   html.AppendLine("          <button id='edit-char' class='small secondary'>Edit</button>");
                                   html.AppendLine("        </div>");
                                   html.AppendLine("      </div>");
                                   html.AppendLine("      <div class='sheet-stats'>");
                                   html.AppendLine("        <div><span>STR</span><strong id='sheet-str'>-</strong></div>");
                                   html.AppendLine("        <div><span>DEX</span><strong id='sheet-dex'>-</strong></div>");
                                   html.AppendLine("        <div><span>CON</span><strong id='sheet-con'>-</strong></div>");
                                   html.AppendLine("        <div><span>INT</span><strong id='sheet-int'>-</strong></div>");
                                   html.AppendLine("        <div><span>WIS</span><strong id='sheet-wis'>-</strong></div>");
                                   html.AppendLine("        <div><span>CHA</span><strong id='sheet-cha'>-</strong></div>");
                                   html.AppendLine("      </div>");
                                   html.AppendLine("      <div class='inventory-section'>");
                                   html.AppendLine("        <div class='inventory-title'>Inventory</div>");
                                   html.AppendLine("        <div id='inventory-grid' class='inventory-grid'></div>");
                                   html.AppendLine("      </div>");
                                   html.AppendLine("      <p class='muted'>Inventory syncing from the host will appear here later.</p>");
                                   html.AppendLine("    </div>");
                                   html.AppendLine("  </section>");
                                   html.AppendLine("  <section id='view-action' class='view'>");
                                   html.AppendLine("    <div class='card'>");
                                   html.AppendLine("      <h3>Action</h3>");
                                   html.AppendLine("      <textarea id='msg' placeholder='Your action...'></textarea>");
                                   html.AppendLine("      <div style='margin-top:6px'><button id='send'>Send</button></div>");
                                   html.AppendLine("    </div>");
                                   html.AppendLine("  </section>");
                                   html.AppendLine("  <section id='view-text' class='view'>");
                                   html.AppendLine("    <div class='card'>");
                                   html.AppendLine("      <h3>Table Stream</h3>");
                                   html.AppendLine("      <div id='log' class='log'></div>");
                                   html.AppendLine("    </div>");
                                   html.AppendLine("  </section>");
                                   html.AppendLine("<script>");
                                   html.AppendLine("const nameV = " + nameLiteral + ";");
                                   html.AppendLine("const codeV = " + codeLiteral + ";");
                                   html.AppendLine("const navButtons = document.querySelectorAll('.tab-btn');");
                                   html.AppendLine("const views = document.querySelectorAll('.view');");
                                   html.AppendLine("function showView(target) {");
                                   html.AppendLine("  views.forEach(view => view.classList.toggle('active', view.id === 'view-' + target));");
                                   html.AppendLine("  navButtons.forEach(btn => btn.classList.toggle('active', btn.dataset.target === target));");
                                   html.AppendLine("}");
                                   html.AppendLine("navButtons.forEach(btn => btn.addEventListener('click', () => showView(btn.dataset.target)));");
                                   html.AppendLine("const createPanel = document.getElementById('char-create');");
                                   html.AppendLine("const sheetPanel = document.getElementById('char-sheet');");
                                   html.AppendLine("const charStatus = document.getElementById('char-status');");
                                   html.AppendLine("const saveBtn = document.getElementById('save');");
                                   html.AppendLine("const editBtn = document.getElementById('edit-char');");
                                   html.AppendLine("const refreshBtn = document.getElementById('refresh-char');");
                                   html.AppendLine("const fieldMap = { Name: 'pc_name', Race: 'pc_race', Class: 'pc_class', Level: 'pc_level', STR: 'pc_str', DEX: 'pc_dex', CON: 'pc_con', INT: 'pc_int', WIS: 'pc_wis', CHA: 'pc_cha' };");
                                   html.AppendLine("let currentPc = null;");
                                   html.AppendLine("function setStatus(text, color) {");
                                   html.AppendLine("  charStatus.textContent = text || ''; charStatus.style.color = color || '#274064';");
                                   html.AppendLine("}");
                                   html.AppendLine("function fillForm(pc) {");
                                   html.AppendLine("  Object.entries(fieldMap).forEach(([key, id]) => {");
                                   html.AppendLine("    const input = document.getElementById(id); if (!input) return;");
                                   html.AppendLine("    if (pc && pc[key] !== undefined && pc[key] !== null) { input.value = pc[key]; }");
                                   html.AppendLine("    else if (key === 'Level') input.value = 1; else if (['STR','DEX','CON','INT','WIS','CHA'].includes(key)) input.value = 10; else input.value = ''; ");
                                   html.AppendLine("  });");
                                   html.AppendLine("}");
                                   html.AppendLine("function collectForm() {");
                                   html.AppendLine("  const result = {}; Object.entries(fieldMap).forEach(([key, id]) => {");
                                   html.AppendLine("    const input = document.getElementById(id); if (!input) return;");
                                   html.AppendLine("    if (['STR','DEX','CON','INT','WIS','CHA','Level'].includes(key)) {");
                                   html.AppendLine("      const parsed = Number(input.value); result[key] = Number.isFinite(parsed) && parsed > 0 ? parsed : (key === 'Level' ? 1 : 10);");
                                   html.AppendLine("    } else { result[key] = input.value.trim(); }");
                                   html.AppendLine("  }); return result;");
                                   html.AppendLine("}");
                                   html.AppendLine("function renderSheet(pc) {");
                                   html.AppendLine("  const title = pc.Name && pc.Name.trim() ? pc.Name.trim() : 'Unnamed Adventurer';");
                                   html.AppendLine("  document.getElementById('sheet-header').textContent = title;");
                                   html.AppendLine("  const meta = (pc.Race && pc.Race.trim() ? pc.Race.trim() : 'Unknown') + ' ' + (pc.Class && pc.Class.trim() ? pc.Class.trim() : 'Adventurer') + ' (Lv ' + (pc.Level || 1) + ')';");
                                   html.AppendLine("  document.getElementById('sheet-meta').textContent = meta;");
                                   html.AppendLine("  ['STR','DEX','CON','INT','WIS','CHA'].forEach(stat => {");
                                   html.AppendLine("    const el = document.getElementById('sheet-' + stat.toLowerCase());");
                                   html.AppendLine("    if (el) { const value = Number(pc[stat] ?? 0); el.textContent = Number.isFinite(value) ? value : 0; }");
                                   html.AppendLine("  });");
                                   html.AppendLine("}");
                                   html.AppendLine("function renderInventory(inventory) {");
                                   html.AppendLine("  const grid = document.getElementById('inventory-grid');");
                                   html.AppendLine("  if (!grid) return;");
                                   html.AppendLine("  grid.innerHTML = '';");
                                   html.AppendLine("  const slots = inventory && (inventory.Slots || inventory.slots) ? (inventory.Slots || inventory.slots) : [];");
                                   html.AppendLine("  const total = 49;");
                                   html.AppendLine("  for (let i = 0; i < total; i++) {");
                                   html.AppendLine("    const data = slots[i] || {};");
                                   html.AppendLine("    const cell = document.createElement('div');");
                                   html.AppendLine("    cell.className = 'inventory-cell';");
                                   html.AppendLine("    const name = data.Name || data.name || '';");
                                   html.AppendLine("    if (!name) cell.classList.add('empty');");
                                   html.AppendLine("    const label = document.createElement('div');");
                                   html.AppendLine("    label.textContent = name;");
                                   html.AppendLine("    cell.appendChild(label);");
                                   html.AppendLine("    const qty = data.Quantity ?? data.quantity ?? 0;");
                                   html.AppendLine("    if (qty > 1) {");
                                   html.AppendLine("      const qtyDiv = document.createElement('div');");
                                   html.AppendLine("      qtyDiv.className = 'qty';");
                                   html.AppendLine("      qtyDiv.textContent = 'x' + qty;");
                                   html.AppendLine("      cell.appendChild(qtyDiv);");
                                   html.AppendLine("    }");
                                   html.AppendLine("    grid.appendChild(cell);");
                                   html.AppendLine("  }");
                                   html.AppendLine("}");
                                   html.AppendLine("function showForm(pc) { if (pc) fillForm(pc); createPanel.style.display = 'block'; sheetPanel.style.display = 'none'; setStatus('', '#274064'); const grid = document.getElementById('inventory-grid'); if (grid) grid.innerHTML=''; showView('character'); }");
                                   html.AppendLine("function showSheet(pc) { currentPc = pc; renderSheet(pc); renderInventory(pc?.Inventory); createPanel.style.display = 'none'; sheetPanel.style.display = 'block'; setStatus('', '#274064'); showView('character'); }");
                                   html.AppendLine("function hasCharacter(pc) { if (!pc) return false; if (pc.Name && pc.Name.trim()) return true; return ['STR','DEX','CON','INT','WIS','CHA'].some(stat => Number(pc[stat] || 0) > 0); }");
                                   html.AppendLine("async function loadCharacter() {");
                                   html.AppendLine("  try {");
                                   html.AppendLine("    const response = await fetch(`/character?name=${encodeURIComponent(nameV)}&code=${encodeURIComponent(codeV)}`);");
                                   html.AppendLine("    if (!response.ok) { showForm(); return; }");
                                   html.AppendLine("    const pc = await response.json(); if (hasCharacter(pc)) showSheet(pc); else showForm();");
                                   html.AppendLine("  } catch (err) { console.error('Failed to load character', err); showForm(); }");
                                   html.AppendLine("}");
                                   html.AppendLine("async function saveCharacter() {");
                                   html.AppendLine("  const pc = collectForm(); setStatus('Saving...', '#274064');");
                                   html.AppendLine("  try {");
                                   html.AppendLine("    const response = await fetch('/character', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ code: codeV, name: nameV, pc }) });");
                                   html.AppendLine("    if (response.ok) { setStatus('Character saved.', '#2a662a'); showSheet(pc); } else { setStatus('Save failed. Try again.', '#a33'); }");
                                   html.AppendLine("  } catch (err) { console.error('Failed to save character', err); setStatus('Network error while saving.', '#a33'); }");
                                   html.AppendLine("}");
                                   html.AppendLine("async function generateCharacter(type) {");
                                   html.AppendLine("  try {");
                                   html.AppendLine("    const response = await fetch('/generate-character', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ type }) });");
                                   html.AppendLine("    if (response.ok) {");
                                   html.AppendLine("      const generated = await response.json();");
                                   html.AppendLine("      const mapped = { Name: generated.name || '', Race: generated.race || '', Class: generated['class'] || '', Level: 1, STR: generated.stats?.str ?? 10, DEX: generated.stats?.dex ?? 10, CON: generated.stats?.con ?? 10, INT: generated.stats?.int ?? 10, WIS: generated.stats?.wis ?? 10, CHA: generated.stats?.cha ?? 10, Inventory: currentPc?.Inventory }; ");
                                   html.AppendLine("      showForm(mapped); setStatus('Character template generated. Review and save when ready.', '#274064');");
                                   html.AppendLine("    }");
                                   html.AppendLine("  } catch (err) { console.error('Failed to generate character', err); setStatus('Failed to generate character.', '#a33'); }");
                                   html.AppendLine("}");
                                   html.AppendLine("window.generateCharacter = generateCharacter;");
                                   html.AppendLine("if (saveBtn) saveBtn.addEventListener('click', e => { e.preventDefault(); saveCharacter(); });");
                                   html.AppendLine("if (editBtn) editBtn.addEventListener('click', () => showForm(currentPc || null));");
                                   html.AppendLine("if (refreshBtn) refreshBtn.addEventListener('click', () => loadCharacter());");
                                   html.AppendLine("const msg = document.getElementById('msg');");
                                   html.AppendLine("const send = document.getElementById('send');");
                                   html.AppendLine("if (send) { send.addEventListener('click', async () => { if (!msg) return; const text = msg.value.trim(); if (!text) return; msg.value = ''; await fetch('/input', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ code: codeV, name: nameV, text }) }); }); }");
                                   html.AppendLine("if (msg) { msg.addEventListener('keypress', e => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); if (send) send.click(); } }); }");
                                   html.AppendLine("const log = document.getElementById('log');");
                                   html.AppendLine("async function loadHistory() { try { const response = await fetch('/history'); if (response.ok) { const history = await response.json(); if (log) { log.textContent = ''; history.forEach(message => { if (message.content && message.content.trim()) { log.textContent += message.role + ': ' + message.content + '\\n'; } }); log.scrollTop = log.scrollHeight; } } } catch (err) { console.error('Failed to load history', err); } }");
                                   html.AppendLine("if (log) { loadHistory(); const es = new EventSource('/stream'); es.onmessage = e => { log.textContent += e.data; log.scrollTop = log.scrollHeight; }; es.onerror = () => { console.log('Stream disconnected, attempting to reconnect...'); }; }");
                                   html.AppendLine("showView('character');");
                                   html.AppendLine("loadCharacter();");
                                   html.AppendLine("</script>");
                                   html.AppendLine("</body></html>");

                                   await ctx.Response.WriteAsync(html.ToString());
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

                                       PlayerCharacter model;
                                       if (_coordinator.TryGetCharacter(code, name, out var existing))
                                       {
                                           model = existing;
                                       }
                                       else
                                       {
                                           model = new PlayerCharacter();
                                       }

                                       model.Name  = pc.GetProperty("Name").GetString() ?? "";
                                       model.Race  = pc.GetProperty("Race").GetString() ?? "";
                                       model.Class = pc.GetProperty("Class").GetString() ?? "";
                                       model.Level = pc.TryGetProperty("Level", out var level) ? level.GetInt32() : 1;
                                       model.STR = pc.TryGetProperty("STR", out var str) ? str.GetInt32() : 0;
                                       model.DEX = pc.TryGetProperty("DEX", out var dex) ? dex.GetInt32() : 0;
                                       model.CON = pc.TryGetProperty("CON", out var con) ? con.GetInt32() : 0;
                                       model.INT = pc.TryGetProperty("INT", out var intel) ? intel.GetInt32() : 0;
                                       model.WIS = pc.TryGetProperty("WIS", out var wis) ? wis.GetInt32() : 0;
                                       model.CHA = pc.TryGetProperty("CHA", out var cha) ? cha.GetInt32() : 0;

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
                                   try
                                   {
                                       using var sr = new StreamReader(ctx.Request.Body);
                                       var body = await sr.ReadToEndAsync();
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
                                   try
                                   {
                                       using var sr = new StreamReader(ctx.Request.Body);
                                       var body = await sr.ReadToEndAsync();
                                       using var doc = JsonDocument.Parse(body);
                                       var expr = doc.RootElement.GetProperty("expression").GetString() ?? "1d20";
                                       
                                       var result = DiceService.Roll(expr);
                                       
                                       var response = new
                                       {
                                           expression = result.Expression,
                                           rolls = result.Rolls,
                                           total = result.Total,
                                           description = result.Description
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

                               // GET /health
                               endpoints.MapGet("/health", async ctx =>
                               {
                                   await ctx.Response.WriteAsync("ok");
                               });

                               // GET /history - Returns full conversation history
                               endpoints.MapGet("/history", async ctx =>
                               {
                                   ctx.Response.ContentType = "application/json";
                                   var history = MessageHistoryService.GetMessagesForWeb();
                                   await ctx.Response.WriteAsync(JsonSerializer.Serialize(history));
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
