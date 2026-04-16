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
using System.Collections.Generic;
using NovaGM.Models;
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
                    web.UseKestrel(opts => opts.Limits.MaxRequestBodySize = 524288) // 512 KB — character sheets with full inventories can be large
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
                                   // QR encodes only the base URL — the room code is displayed separately
                                   // and must be typed by the player. Sharing the URL alone does not
                                   // grant access to the room.
                                   var qrDataUrl = "";
                                   try
                                   {
                                       var baseUrl = AllowLan && LanIps.Length > 0
                                           ? $"http://{LanIps[0]}:{Port}"
                                           : $"http://127.0.0.1:{Port}";
                                       qrDataUrl = QRCodeService.GenerateQRCodeDataUrl(baseUrl);
                                   }
                                   catch { /* QR generation failed, continue without */ }

                                   await ctx.Response.WriteAsync($@"<!doctype html>
<html><head><meta charset='utf-8'><title>NovaGM — Join</title>
<style>body{{font-family:sans-serif;margin:2rem;}}input,button{{font-size:1rem;}}</style></head>
<body>
  <h2>Join NovaGM</h2>
  {(string.IsNullOrEmpty(qrDataUrl) ? "" : $"<p><img src='{qrDataUrl}' alt='QR Code' style='border:1px solid #ddd;'/></p>")}
  <form action='/hud' method='get'>
    <label>Your name: <input name='name' required maxlength='64'></label>
    <label style='margin-left:1rem'>Room code: <input name='code' required maxlength='6' style='text-transform:uppercase'></label>
    <button type='submit' style='margin-left:1rem'>Continue</button>
  </form>
  <p style='margin-top:1rem;color:#666'>Already joined? Go to the Action tab in the HUD.</p>
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
    const response = await fetch('/history?code=' + encodeURIComponent('{codeJs}'));
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
const es = new EventSource('/stream?code=' + encodeURIComponent('{codeJs}'));
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

                                   var nameEncoded = WebUtility.HtmlEncode(name);
                                   var html = """
<!doctype html>
<html><head>
<meta charset='utf-8'>
<title>NovaGM — HUD (NAME_ENCODED)</title>
<meta name='viewport' content='width=device-width, initial-scale=1'/>
<style>
:root {
  --surface:      #121417;
  --surface-alt:  #1A1D22;
  --surface-deep: #0D0F12;
  --border:       #2B3448;
  --text:         #EAECEE;
  --muted:        #A9B2BD;
  --primary:      #5A66FF;
  --accent-player:#4A9E6A;
  --accent-sys:   #D4961A;
}
* { box-sizing: border-box; }
body { font-family: 'Segoe UI', sans-serif; margin: 0; background: var(--surface-deep); color: var(--text); min-height: 100vh; }
.nav { display: flex; gap: 0; background: var(--surface); border-bottom: 1px solid var(--border); }
.tab-btn { flex: 1; background: transparent; border: none; border-bottom: 2px solid transparent; padding: 10px 0; cursor: pointer; font-size: 14px; color: var(--muted); transition: color 0.2s, border-color 0.2s; }
.tab-btn.active { color: var(--primary); border-bottom-color: var(--primary); }
.tab-btn:hover:not(.active) { color: var(--text); }
.view { display: none; padding: 12px; }
.view.active { display: flex; flex-direction: column; min-height: calc(100vh - 44px); }
.card { background: var(--surface-alt); border: 1px solid var(--border); border-radius: 10px; padding: 14px; margin-bottom: 12px; }
h3 { margin: 0 0 10px; font-size: 15px; color: var(--text); }
input, textarea { background: var(--surface); border: 1px solid var(--border); border-radius: 6px; color: var(--text); padding: 7px 9px; font-size: 13px; font-family: inherit; width: 100%; }
input:focus, textarea:focus { outline: none; border-color: var(--primary); }
.btn { padding: 7px 16px; border-radius: 6px; border: none; background: var(--primary); color: #fff; cursor: pointer; font-size: 13px; }
.btn:hover { opacity: 0.88; }
.btn.secondary { background: var(--surface); border: 1px solid var(--border); color: var(--muted); }
.btn.small { padding: 4px 10px; font-size: 12px; }
.btn.gen { background: var(--surface-deep); border: 1px solid var(--border); color: var(--accent-player); font-size: 12px; padding: 5px 11px; }
.btn.gen:hover { border-color: var(--accent-player); }
.form-row { display: flex; gap: 8px; flex-wrap: wrap; margin-bottom: 8px; }
.form-row > label { display: flex; flex-direction: column; gap: 4px; font-size: 12px; color: var(--muted); flex: 1; min-width: 90px; }
.gen-row { display: flex; gap: 6px; flex-wrap: wrap; padding: 8px; background: var(--surface-deep); border: 1px solid var(--border); border-radius: 8px; margin-bottom: 10px; }
#char-status { margin-top: 8px; font-size: 13px; min-height: 1em; }
.sheet-header { margin-bottom: 12px; overflow: hidden; }
.sheet-controls { float: right; display: flex; gap: 6px; }
.sheet-name { font-size: 22px; font-weight: 700; color: var(--text); }
.sheet-sub { font-size: 13px; color: var(--muted); margin-top: 2px; }
.stat-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 6px; margin-bottom: 14px; }
.stat-cell { background: var(--surface-deep); border: 1px solid var(--border); border-radius: 8px; padding: 8px 4px; text-align: center; }
.stat-label { font-size: 10px; color: var(--muted); display: block; margin-bottom: 2px; letter-spacing: 0.5px; text-transform: uppercase; }
.stat-val { font-size: 20px; font-weight: 700; color: var(--text); display: block; line-height: 1; }
.stat-mod { font-size: 10px; display: block; min-height: 14px; margin-top: 2px; }
.doll-top, .doll-bottom { display: flex; justify-content: center; gap: 6px; margin-bottom: 6px; }
.doll-middle { display: grid; grid-template-columns: 1fr 110px 1fr; gap: 6px; align-items: center; }
.doll-left, .doll-right { display: flex; flex-direction: column; gap: 6px; }
.doll-left { align-items: flex-end; }
.doll-center { display: flex; justify-content: center; }
.doll-silhouette { width: 100px; height: 150px; border: 1px solid var(--border); border-radius: 50px 50px 12px 12px; background: var(--surface-deep); display: flex; align-items: center; justify-content: center; color: var(--muted); font-size: 52px; }
.eq-slot { border: 1px solid var(--border); border-radius: 6px; background: var(--surface-deep); min-height: 48px; width: 90px; display: flex; flex-direction: column; align-items: center; justify-content: center; padding: 5px; font-size: 11px; color: var(--text); text-align: center; cursor: pointer; transition: border-color 0.15s, background 0.15s; }
.eq-slot:hover { border-color: var(--primary); background: var(--surface); }
.eq-slot.equipped { border-color: var(--accent-player); background: #162b1e; }
.eq-slot .slot-lbl { font-size: 9px; color: var(--muted); letter-spacing: 0.3px; margin-bottom: 3px; text-transform: uppercase; }
.eq-slot .slot-item { font-size: 11px; font-weight: 600; color: var(--text); word-break: break-word; }
.inv-grid { display: grid; grid-template-columns: repeat(7, 1fr); gap: 4px; }
.inv-cell { border: 1px solid var(--border); border-radius: 6px; background: var(--surface-deep); min-height: 52px; display: flex; flex-direction: column; align-items: center; justify-content: center; padding: 4px; font-size: 11px; color: var(--text); text-align: center; word-break: break-word; }
.inv-cell.empty { opacity: 0.3; }
.inv-cell .qty { font-size: 10px; color: var(--muted); margin-top: 2px; }
.log { white-space: pre-wrap; font-family: 'Consolas', monospace; font-size: 13px; background: var(--surface-deep); border: 1px solid var(--border); border-radius: 8px; padding: 10px; overflow-y: auto; height: calc(100vh - 185px); color: var(--text); flex: 1; }
.action-bar { margin-top: 10px; display: flex; gap: 8px; align-items: flex-end; }
.action-bar textarea { flex: 1; min-height: 3.5em; resize: vertical; }
</style>
</head>
<body>
<div class='nav'>
  <button class='tab-btn active' data-target='character'>Character</button>
  <button class='tab-btn' data-target='inventory'>Inventory</button>
  <button class='tab-btn' data-target='table'>Table</button>
</div>

<section id='view-character' class='view active'>
  <div id='char-create' class='card'>
    <h3>Create your character</h3>
    <div class='gen-row'>
      <button class='btn gen' onclick='generateCharacter("random")'>Random</button>
      <button class='btn gen' onclick='generateCharacter("fighter")'>Fighter</button>
      <button class='btn gen' onclick='generateCharacter("rogue")'>Rogue</button>
      <button class='btn gen' onclick='generateCharacter("mage")'>Mage</button>
    </div>
    <div class='form-row'>
      <label>Name<input id='pc_name' autocomplete='off'/></label>
      <label>Race<input id='pc_race' autocomplete='off'/></label>
      <label>Class<input id='pc_class' autocomplete='off'/></label>
      <label style='max-width:70px'>Level<input id='pc_level' type='number' min='1' value='1'/></label>
    </div>
    <div class='form-row'>
      <label>STR<input id='pc_str' type='number' value='10'/></label>
      <label>DEX<input id='pc_dex' type='number' value='10'/></label>
      <label>CON<input id='pc_con' type='number' value='10'/></label>
      <label>INT<input id='pc_int' type='number' value='10'/></label>
      <label>WIS<input id='pc_wis' type='number' value='10'/></label>
      <label>CHA<input id='pc_cha' type='number' value='10'/></label>
    </div>
    <div style='margin-top:10px;display:flex;gap:8px;'>
      <button id='save' class='btn'>Save Character</button>
    </div>
    <div id='char-status'></div>
  </div>

  <div id='char-sheet' class='card' style='display:none;'>
    <div class='sheet-header'>
      <div class='sheet-controls'>
        <button id='refresh-char' class='btn small secondary'>Refresh</button>
        <button id='edit-char' class='btn small secondary'>Edit</button>
      </div>
      <div class='sheet-name' id='sheet-header'>Unnamed Adventurer</div>
      <div class='sheet-sub' id='sheet-meta'>Awaiting details</div>
    </div>

    <div class='stat-grid'>
      <div class='stat-cell'><span class='stat-label'>STR</span><span class='stat-val' id='sheet-str'>—</span><span class='stat-mod' id='mod-str'></span></div>
      <div class='stat-cell'><span class='stat-label'>DEX</span><span class='stat-val' id='sheet-dex'>—</span><span class='stat-mod' id='mod-dex'></span></div>
      <div class='stat-cell'><span class='stat-label'>CON</span><span class='stat-val' id='sheet-con'>—</span><span class='stat-mod' id='mod-con'></span></div>
      <div class='stat-cell'><span class='stat-label'>INT</span><span class='stat-val' id='sheet-int'>—</span><span class='stat-mod' id='mod-int'></span></div>
      <div class='stat-cell'><span class='stat-label'>WIS</span><span class='stat-val' id='sheet-wis'>—</span><span class='stat-mod' id='mod-wis'></span></div>
      <div class='stat-cell'><span class='stat-label'>CHA</span><span class='stat-val' id='sheet-cha'>—</span><span class='stat-mod' id='mod-cha'></span></div>
    </div>

    <div class='doll-top'>
      <div class='eq-slot' data-slot='Head' onclick='handleEquipmentClick("Head")'><div class='slot-lbl'>Head</div><div class='slot-item' id='eq-Head'>—</div></div>
      <div class='eq-slot' data-slot='Neck' onclick='handleEquipmentClick("Neck")'><div class='slot-lbl'>Neck</div><div class='slot-item' id='eq-Neck'>—</div></div>
      <div class='eq-slot' data-slot='Cloak' onclick='handleEquipmentClick("Cloak")'><div class='slot-lbl'>Cloak</div><div class='slot-item' id='eq-Cloak'>—</div></div>
    </div>
    <div class='doll-middle'>
      <div class='doll-left'>
        <div class='eq-slot' data-slot='Chest' onclick='handleEquipmentClick("Chest")'><div class='slot-lbl'>Chest</div><div class='slot-item' id='eq-Chest'>—</div></div>
        <div class='eq-slot' data-slot='Hands' onclick='handleEquipmentClick("Hands")'><div class='slot-lbl'>Hands</div><div class='slot-item' id='eq-Hands'>—</div></div>
        <div class='eq-slot' data-slot='MainHand' onclick='handleEquipmentClick("MainHand")'><div class='slot-lbl'>Main Hand</div><div class='slot-item' id='eq-MainHand'>—</div></div>
        <div class='eq-slot' data-slot='Ring1' onclick='handleEquipmentClick("Ring1")'><div class='slot-lbl'>Ring 1</div><div class='slot-item' id='eq-Ring1'>—</div></div>
      </div>
      <div class='doll-center'>
        <div class='doll-silhouette'>&#9876;</div>
      </div>
      <div class='doll-right'>
        <div class='eq-slot' data-slot='Belt' onclick='handleEquipmentClick("Belt")'><div class='slot-lbl'>Belt</div><div class='slot-item' id='eq-Belt'>—</div></div>
        <div class='eq-slot' data-slot='Legs' onclick='handleEquipmentClick("Legs")'><div class='slot-lbl'>Legs</div><div class='slot-item' id='eq-Legs'>—</div></div>
        <div class='eq-slot' data-slot='OffHand' onclick='handleEquipmentClick("OffHand")'><div class='slot-lbl'>Off Hand</div><div class='slot-item' id='eq-OffHand'>—</div></div>
        <div class='eq-slot' data-slot='Ring2' onclick='handleEquipmentClick("Ring2")'><div class='slot-lbl'>Ring 2</div><div class='slot-item' id='eq-Ring2'>—</div></div>
      </div>
    </div>
    <div class='doll-bottom'>
      <div class='eq-slot' data-slot='Feet' onclick='handleEquipmentClick("Feet")'><div class='slot-lbl'>Feet</div><div class='slot-item' id='eq-Feet'>—</div></div>
    </div>
  </div>
</section>

<section id='view-inventory' class='view'>
  <div class='card'>
    <h3>Inventory</h3>
    <div id='inventory-grid' class='inv-grid'></div>
  </div>
</section>

<section id='view-table' class='view'>
  <div class='card' style='flex:1;display:flex;flex-direction:column;'>
    <div id='log' class='log'></div>
    <div class='action-bar'>
      <textarea id='msg' placeholder='Your action... (Enter to send, Shift+Enter for new line)'></textarea>
      <button id='send' class='btn'>Send</button>
    </div>
  </div>
</section>

<script>
const nameV = NAME_LITERAL;
const codeV = CODE_LITERAL;

const navButtons = document.querySelectorAll('.tab-btn');
const views = document.querySelectorAll('.view');
function showView(target) {
  views.forEach(v => v.classList.toggle('active', v.id === 'view-' + target));
  navButtons.forEach(b => b.classList.toggle('active', b.dataset.target === target));
}
navButtons.forEach(btn => btn.addEventListener('click', () => showView(btn.dataset.target)));

const createPanel = document.getElementById('char-create');
const sheetPanel  = document.getElementById('char-sheet');
const charStatus  = document.getElementById('char-status');
const saveBtn     = document.getElementById('save');
const editBtn     = document.getElementById('edit-char');
const refreshBtn  = document.getElementById('refresh-char');

const fieldMap = { Name:'pc_name', Race:'pc_race', Class:'pc_class', Level:'pc_level',
                   STR:'pc_str', DEX:'pc_dex', CON:'pc_con', INT:'pc_int', WIS:'pc_wis', CHA:'pc_cha' };
let currentPc = null;

function setStatus(text, color) {
  charStatus.textContent = text || '';
  charStatus.style.color = color || '#EAECEE';
}

function fillForm(pc) {
  Object.entries(fieldMap).forEach(([key, id]) => {
    const input = document.getElementById(id);
    if (!input) return;
    if (pc && pc[key] !== undefined && pc[key] !== null) input.value = pc[key];
    else if (key === 'Level') input.value = 1;
    else if (['STR','DEX','CON','INT','WIS','CHA'].includes(key)) input.value = 10;
    else input.value = '';
  });
}

function collectForm() {
  const result = {};
  Object.entries(fieldMap).forEach(([key, id]) => {
    const input = document.getElementById(id);
    if (!input) return;
    if (['STR','DEX','CON','INT','WIS','CHA','Level'].includes(key)) {
      const parsed = Number(input.value);
      result[key] = Number.isFinite(parsed) && parsed > 0 ? parsed : (key === 'Level' ? 1 : 10);
    } else {
      result[key] = input.value.trim();
    }
  });
  return result;
}

function calculateStatModifiers(equipment) {
  const mods = { STR:0, DEX:0, CON:0, INT:0, WIS:0, CHA:0 };
  if (!equipment) return mods;
  Object.values(equipment).forEach(item => {
    if (item && item.StatMods) {
      Object.entries(item.StatMods).forEach(([stat, value]) => {
        const key = stat.toUpperCase();
        if (Object.prototype.hasOwnProperty.call(mods, key)) mods[key] += Number(value || 0);
      });
    }
  });
  return mods;
}

function renderSheet(pc) {
  const title = pc.Name && pc.Name.trim() ? pc.Name.trim() : 'Unnamed Adventurer';
  document.getElementById('sheet-header').textContent = title;
  const meta = (pc.Race && pc.Race.trim() ? pc.Race.trim() : 'Unknown') + ' ' +
               (pc.Class && pc.Class.trim() ? pc.Class.trim() : 'Adventurer') + ' (Lv ' + (pc.Level || 1) + ')';
  document.getElementById('sheet-meta').textContent = meta;

  const statMods = calculateStatModifiers(pc.Equipment || {});
  ['STR','DEX','CON','INT','WIS','CHA'].forEach(stat => {
    const valEl = document.getElementById('sheet-' + stat.toLowerCase());
    const modEl = document.getElementById('mod-' + stat.toLowerCase());
    const base  = Number(pc[stat] ?? 0);
    const bonus = statMods[stat] || 0;
    const total = base + bonus;
    if (valEl) valEl.textContent = Number.isFinite(base) ? total : 0;
    if (modEl) {
      if (bonus !== 0) {
        modEl.textContent = (bonus > 0 ? '+' : '') + bonus + ' from gear';
        modEl.style.color = bonus > 0 ? '#4A9E6A' : '#D4961A';
      } else {
        modEl.textContent = '';
      }
    }
  });
}

function renderEquipment(equipment) {
  const slots = ['Head','Neck','Cloak','Chest','Hands','Belt','Legs','Feet','MainHand','OffHand','Ring1','Ring2'];
  slots.forEach(slot => {
    const nameEl = document.getElementById('eq-' + slot);
    const slotEl = document.querySelector('[data-slot="' + slot + '"]');
    if (!nameEl || !slotEl) return;
    const item = equipment && equipment[slot] ? equipment[slot] : null;
    if (item && item.Name) {
      nameEl.textContent = item.Name;
      slotEl.classList.add('equipped');
    } else {
      nameEl.textContent = '—';
      slotEl.classList.remove('equipped');
    }
  });
}

function renderInventory(inventory) {
  const grid = document.getElementById('inventory-grid');
  if (!grid) return;
  grid.innerHTML = '';
  const slots = inventory && (inventory.Slots || inventory.slots) ? (inventory.Slots || inventory.slots) : [];
  for (let i = 0; i < 49; i++) {
    const data = slots[i] || {};
    const cell = document.createElement('div');
    cell.className = 'inv-cell';
    const name = data.Name || data.name || '';
    if (!name) cell.classList.add('empty');
    const label = document.createElement('div');
    label.textContent = name;
    cell.appendChild(label);
    const qty = data.Quantity ?? data.quantity ?? 0;
    if (qty > 1) {
      const qd = document.createElement('div');
      qd.className = 'qty';
      qd.textContent = 'x' + qty;
      cell.appendChild(qd);
    }
    grid.appendChild(cell);
  }
}

async function handleEquipmentClick(slot) {
  if (!currentPc) return;
  const equipment = currentPc.Equipment || {};
  const isEquipped = equipment[slot] && equipment[slot].Name;
  if (isEquipped) {
    if (!confirm('Unequip ' + equipment[slot].Name + '?')) return;
    try {
      const r = await fetch('/equipment/unequip', { method:'POST', headers:{'Content-Type':'application/json'},
        body: JSON.stringify({ code: codeV, name: nameV, slot }) });
      if (r.ok) await loadCharacter(); else alert('Failed to unequip item');
    } catch (err) { console.error('Unequip error:', err); alert('Error unequipping item'); }
  } else {
    const inventory = currentPc.Inventory;
    const items = inventory && inventory.Slots ? inventory.Slots.filter(s => s && s.Name) : [];
    if (items.length === 0) { alert('No items in inventory to equip'); return; }
    let itemList = 'Select item to equip:\n\n';
    items.forEach((item, idx) => { itemList += (idx+1) + '. ' + item.Name + ' (x' + (item.Quantity||1) + ')\n'; });
    const selection = prompt(itemList + '\nEnter item number:');
    if (!selection) return;
    const itemIdx = parseInt(selection) - 1;
    if (itemIdx < 0 || itemIdx >= items.length) { alert('Invalid selection'); return; }
    const selectedItem = items[itemIdx];
    try {
      const r = await fetch('/equipment/equip', { method:'POST', headers:{'Content-Type':'application/json'},
        body: JSON.stringify({ code: codeV, name: nameV, itemId: selectedItem.ItemId, slot }) });
      if (r.ok) await loadCharacter(); else alert('Failed to equip item');
    } catch (err) { console.error('Equip error:', err); alert('Error equipping item'); }
  }
}

function showForm(pc) {
  if (pc) fillForm(pc);
  createPanel.style.display = 'block';
  sheetPanel.style.display = 'none';
  setStatus('', '');
  showView('character');
}

function showSheet(pc) {
  currentPc = pc;
  renderSheet(pc);
  renderEquipment(pc && pc.Equipment ? pc.Equipment : {});
  createPanel.style.display = 'none';
  sheetPanel.style.display = 'block';
  setStatus('', '');
  showView('character');
}

function hasCharacter(pc) {
  if (!pc) return false;
  if (pc.Name && pc.Name.trim()) return true;
  return ['STR','DEX','CON','INT','WIS','CHA'].some(stat => Number(pc[stat] || 0) > 0);
}

async function loadCharacter() {
  try {
    const r = await fetch('/character?name=' + encodeURIComponent(nameV) + '&code=' + encodeURIComponent(codeV));
    if (!r.ok) { showForm(); return; }
    const pc = await r.json();
    if (hasCharacter(pc)) showSheet(pc); else showForm();
  } catch (err) { console.error('Failed to load character', err); showForm(); }
}

async function saveCharacter() {
  const pc = collectForm();
  setStatus('Saving...', '#A9B2BD');
  try {
    const r = await fetch('/character', { method:'POST', headers:{'Content-Type':'application/json'},
      body: JSON.stringify({ code: codeV, name: nameV, pc }) });
    if (r.ok) { setStatus('Saved.', '#4A9E6A'); showSheet(pc); }
    else setStatus('Save failed. Try again.', '#D4961A');
  } catch (err) { console.error(err); setStatus('Network error while saving.', '#D4961A'); }
}

async function generateCharacter(type) {
  try {
    const r = await fetch('/generate-character', { method:'POST', headers:{'Content-Type':'application/json'},
      body: JSON.stringify({ code: codeV, type }) });
    if (r.ok) {
      const g = await r.json();
      const mapped = { Name: g.name||'', Race: g.race||'', Class: g['class']||'', Level: 1,
                       STR: g.stats ? (g.stats.str ?? 10) : 10,
                       DEX: g.stats ? (g.stats.dex ?? 10) : 10,
                       CON: g.stats ? (g.stats.con ?? 10) : 10,
                       INT: g.stats ? (g.stats.int ?? 10) : 10,
                       WIS: g.stats ? (g.stats.wis ?? 10) : 10,
                       CHA: g.stats ? (g.stats.cha ?? 10) : 10,
                       Inventory: currentPc ? currentPc.Inventory : null };
      showForm(mapped);
      setStatus('Template generated. Review and save.', '#A9B2BD');
    }
  } catch (err) { console.error(err); setStatus('Failed to generate.', '#D4961A'); }
}
window.generateCharacter = generateCharacter;

if (saveBtn)    saveBtn.addEventListener('click', e => { e.preventDefault(); saveCharacter(); });
if (editBtn)    editBtn.addEventListener('click', () => showForm(currentPc || null));
if (refreshBtn) refreshBtn.addEventListener('click', () => loadCharacter());

document.querySelector('[data-target="inventory"]').addEventListener('click', () => {
  if (currentPc) renderInventory(currentPc.Inventory);
});

const msg  = document.getElementById('msg');
const send = document.getElementById('send');
if (send) {
  send.addEventListener('click', async () => {
    if (!msg) return;
    const text = msg.value.trim();
    if (!text) return;
    msg.value = '';
    await fetch('/input', { method:'POST', headers:{'Content-Type':'application/json'},
      body: JSON.stringify({ code: codeV, name: nameV, text }) });
  });
}
if (msg) {
  msg.addEventListener('keydown', e => {
    if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); if (send) send.click(); }
  });
}

const log = document.getElementById('log');
async function loadHistory() {
  try {
    const r = await fetch('/history?code=' + encodeURIComponent(codeV));
    if (r.ok) {
      const history = await r.json();
      if (log) {
        log.textContent = '';
        history.forEach(message => {
          if (message.content && message.content.trim())
            log.textContent += message.role + ': ' + message.content + '\n';
        });
        log.scrollTop = log.scrollHeight;
      }
    }
  } catch (err) { console.error('Failed to load history', err); }
}

if (log) {
  loadHistory();
  const es = new EventSource('/stream?code=' + encodeURIComponent(codeV));
  es.onmessage = e => { log.textContent += e.data; log.scrollTop = log.scrollHeight; };
  es.onerror = () => console.log('Stream disconnected, reconnecting...');
}

showView('character');
loadCharacter();
</script>
</body></html>
"""
                                   .Replace("NAME_ENCODED", nameEncoded)
                                   .Replace("NAME_LITERAL", nameLiteral)
                                   .Replace("CODE_LITERAL", codeLiteral);
                                   await ctx.Response.WriteAsync(html);
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

                                       if (name.Length > 64)
                                       {
                                           ctx.Response.StatusCode = 400;
                                           await ctx.Response.WriteAsync("name too long");
                                           return;
                                       }

                                       var pcName = pc.GetProperty("Name").GetString() ?? "";
                                       if (pcName.Length > 64)
                                       {
                                           ctx.Response.StatusCode = 400;
                                           await ctx.Response.WriteAsync("character name too long");
                                           return;
                                       }

                                       PlayerCharacter model;
                                       if (_coordinator.TryGetCharacter(code, name, out var existing))
                                       {
                                           model = existing;
                                       }
                                       else
                                       {
                                           model = new PlayerCharacter();
                                       }

                                       model.Name  = pcName;
                                       model.Race  = (pc.GetProperty("Race").GetString() ?? "")[..Math.Min(64, (pc.GetProperty("Race").GetString() ?? "").Length)];
                                       model.Class = (pc.GetProperty("Class").GetString() ?? "")[..Math.Min(64, (pc.GetProperty("Class").GetString() ?? "").Length)];
                                       model.Level = pc.TryGetProperty("Level", out var level) ? level.GetInt32() : 1;
                                       model.STR = pc.TryGetProperty("STR", out var str) ? str.GetInt32() : 0;
                                       model.DEX = pc.TryGetProperty("DEX", out var dex) ? dex.GetInt32() : 0;
                                       model.CON = pc.TryGetProperty("CON", out var con) ? con.GetInt32() : 0;
                                       model.INT = pc.TryGetProperty("INT", out var intel) ? intel.GetInt32() : 0;
                                       model.WIS = pc.TryGetProperty("WIS", out var wis) ? wis.GetInt32() : 0;
                                       model.CHA = pc.TryGetProperty("CHA", out var cha) ? cha.GetInt32() : 0;

                                       _coordinator.SetCharacter(code, name, model);
                                       // Wake the main window's input loop so it can update
                                       // ConnectedPlayers / RemotePlayers immediately — without
                                       // waiting for the player to send their first message.
                                       // Empty text is the sentinel; HandleTurnAsync skips it.
                                       _coordinator.TryEnqueue(code, name, "");
                                       ctx.Response.StatusCode = 204;
                                   }
                                   catch
                                   {
                                       ctx.Response.StatusCode = 400;
                                       await ctx.Response.WriteAsync("bad json");
                                   }
                               });
                               
                               // POST /equipment/equip { code, name, itemId, slot }
                               endpoints.MapPost("/equipment/equip", async ctx =>
                               {
                                   try
                                   {
                                       using var sr = new StreamReader(ctx.Request.Body);
                                       var body = await sr.ReadToEndAsync();
                                       using var doc = JsonDocument.Parse(body);
                                       var root = doc.RootElement;
                                       
                                       var code = root.GetProperty("code").GetString() ?? "";
                                       var name = root.GetProperty("name").GetString() ?? "";
                                       var itemId = root.GetProperty("itemId").GetString() ?? "";
                                       var slotName = root.GetProperty("slot").GetString() ?? "";
                                       
                                       if (!Enum.TryParse<EquipmentSlot>(slotName, out var slot))
                                       {
                                           ctx.Response.StatusCode = 400;
                                           await ctx.Response.WriteAsync("invalid slot");
                                           return;
                                       }
                                       
                                       if (!_coordinator.TryGetCharacter(code, name, out var pc))
                                       {
                                           ctx.Response.StatusCode = 404;
                                           await ctx.Response.WriteAsync("character not found");
                                           return;
                                       }
                                       
                                       // Find item in inventory
                                       var inventorySlot = pc.Inventory.Slots.FirstOrDefault(s => 
                                           s?.ItemId?.Equals(itemId, StringComparison.OrdinalIgnoreCase) == true);
                                       
                                       if (inventorySlot == null)
                                       {
                                           ctx.Response.StatusCode = 404;
                                           await ctx.Response.WriteAsync("item not found in inventory");
                                           return;
                                       }
                                       
                                       // Unequip existing item if slot is occupied
                                       if (pc.Equipment.ContainsKey(slot))
                                       {
                                           var existingItem = pc.Equipment[slot];
                                           var entry = new InventoryEntry(
                                               existingItem.Name.ToLowerInvariant().Replace(" ", "_"),
                                               existingItem.Name,
                                               1,
                                               null,
                                               existingItem.StatMods
                                           );
                                           
                                           if (!pc.Inventory.TryAdd(entry))
                                           {
                                               ctx.Response.StatusCode = 400;
                                               await ctx.Response.WriteAsync("inventory full");
                                               return;
                                           }
                                           
                                           pc.Equipment.Remove(slot);
                                       }
                                       
                                       // Create Item from InventoryEntry
                                       var item = new Item
                                       {
                                           Name = inventorySlot.Name,
                                           Slot = slot,
                                           StatMods = new Dictionary<string, int>(inventorySlot.Modifiers),
                                           Description = $"{inventorySlot.Name} equipped to {slot}"
                                       };
                                       
                                       // Add to equipment
                                       pc.Equipment[slot] = item;
                                       
                                       // Remove from inventory
                                       pc.Inventory.Remove(itemId, 1);
                                       
                                       ctx.Response.ContentType = "application/json";
                                       await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { success = true }));
                                   }
                                   catch (Exception ex)
                                   {
                                       Console.Error.WriteLine($"[equipment/equip] {ex}");
                                       ctx.Response.StatusCode = 500;
                                       await ctx.Response.WriteAsync("internal error");
                                   }
                               });

                               // POST /equipment/unequip { code, name, slot }
                               endpoints.MapPost("/equipment/unequip", async ctx =>
                               {
                                   try
                                   {
                                       using var sr = new StreamReader(ctx.Request.Body);
                                       var body = await sr.ReadToEndAsync();
                                       using var doc = JsonDocument.Parse(body);
                                       var root = doc.RootElement;
                                       
                                       var code = root.GetProperty("code").GetString() ?? "";
                                       var name = root.GetProperty("name").GetString() ?? "";
                                       var slotName = root.GetProperty("slot").GetString() ?? "";
                                       
                                       if (!Enum.TryParse<EquipmentSlot>(slotName, out var slot))
                                       {
                                           ctx.Response.StatusCode = 400;
                                           await ctx.Response.WriteAsync("invalid slot");
                                           return;
                                       }
                                       
                                       if (!_coordinator.TryGetCharacter(code, name, out var pc))
                                       {
                                           ctx.Response.StatusCode = 404;
                                           await ctx.Response.WriteAsync("character not found");
                                           return;
                                       }
                                       
                                       if (!pc.Equipment.TryGetValue(slot, out var item))
                                       {
                                           ctx.Response.StatusCode = 400;
                                           await ctx.Response.WriteAsync("slot is empty");
                                           return;
                                       }
                                       
                                       // Create inventory entry
                                       var entry = new InventoryEntry(
                                           item.Name.ToLowerInvariant().Replace(" ", "_"),
                                           item.Name,
                                           1,
                                           null,
                                           item.StatMods
                                       );
                                       
                                       if (!pc.Inventory.TryAdd(entry))
                                       {
                                           ctx.Response.StatusCode = 400;
                                           await ctx.Response.WriteAsync("inventory full");
                                           return;
                                       }
                                       
                                       // Remove from equipment
                                       pc.Equipment.Remove(slot);
                                       
                                       ctx.Response.ContentType = "application/json";
                                       await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { success = true }));
                                   }
                                   catch (Exception ex)
                                   {
                                       Console.Error.WriteLine($"[equipment/unequip] {ex}");
                                       ctx.Response.StatusCode = 500;
                                       await ctx.Response.WriteAsync("internal error");
                                   }
                               });

                               // GET /stream  (SSE) — linked to app shutdown + this server shutdown
                               endpoints.MapGet("/stream", async ctx =>
                               {
                                   var streamCode = ctx.Request.Query["code"].ToString();
                                   if (!string.Equals(streamCode, _coordinator.CurrentCode, StringComparison.OrdinalIgnoreCase))
                                   {
                                       ctx.Response.StatusCode = 403;
                                       await ctx.Response.WriteAsync("bad room code");
                                       return;
                                   }

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

                                       if (name.Length > 64)
                                       {
                                           ctx.Response.StatusCode = 400;
                                           await ctx.Response.WriteAsync("name too long");
                                           return;
                                       }
                                       if (string.IsNullOrWhiteSpace(text))
                                       {
                                           ctx.Response.StatusCode = 400;
                                           await ctx.Response.WriteAsync("missing text");
                                           return;
                                       }
                                       if (text.Length > 2000)
                                       {
                                           ctx.Response.StatusCode = 400;
                                           await ctx.Response.WriteAsync("text too long");
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

                               // POST /generate-character { code, type }
                               endpoints.MapPost("/generate-character", async ctx =>
                               {
                                   try
                                   {
                                       using var sr = new StreamReader(ctx.Request.Body);
                                       var body = await sr.ReadToEndAsync();
                                       using var doc = JsonDocument.Parse(body);
                                       var code = doc.RootElement.TryGetProperty("code", out var codeProp)
                                           ? codeProp.GetString() ?? ""
                                           : "";
                                       if (!string.Equals(code, _coordinator.CurrentCode, StringComparison.OrdinalIgnoreCase))
                                       {
                                           ctx.Response.StatusCode = 403;
                                           await ctx.Response.WriteAsync("bad room code");
                                           return;
                                       }
                                       var type = doc.RootElement.TryGetProperty("type", out var typeProp)
                                           ? typeProp.GetString() ?? "random"
                                           : "random";

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

                               // POST /dice  { code, expression }
                               endpoints.MapPost("/dice", async ctx =>
                               {
                                   try
                                   {
                                       using var sr = new StreamReader(ctx.Request.Body);
                                       var body = await sr.ReadToEndAsync();
                                       using var doc = JsonDocument.Parse(body);
                                       var diceCode = doc.RootElement.TryGetProperty("code", out var diceCodeProp)
                                           ? diceCodeProp.GetString() ?? "" : "";
                                       if (!string.Equals(diceCode, _coordinator.CurrentCode, StringComparison.OrdinalIgnoreCase))
                                       {
                                           ctx.Response.StatusCode = 403;
                                           await ctx.Response.WriteAsync("bad room code");
                                           return;
                                       }
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

                               // GET /history?code= - Returns full conversation history (room code required)
                               endpoints.MapGet("/history", async ctx =>
                               {
                                   var code = ctx.Request.Query["code"].ToString();
                                   if (!string.Equals(code, _coordinator.CurrentCode, StringComparison.OrdinalIgnoreCase))
                                   {
                                       ctx.Response.StatusCode = 403;
                                       await ctx.Response.WriteAsync("bad room code");
                                       return;
                                   }
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
                // Use a UDP socket to determine which local IP the OS routes LAN traffic through.
                // No data is sent; this just resolves the outbound interface.
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Connect("8.8.8.8", 80);
                var primary = (socket.LocalEndPoint as IPEndPoint)?.Address?.ToString();
                if (!string.IsNullOrEmpty(primary))
                    return new[] { primary };
            }
            catch { }

            // Fallback: return all non-loopback IPv4 addresses
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
