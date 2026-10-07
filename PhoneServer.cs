using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace TS6Overlay;

/// <summary>
/// Panel na telefonie: strona w sieci domowej z listą osób, przyciskami bindów i „zatrzymaj wszystko”.
/// Zwykły TcpListener (HttpListener na wszystkich interfejsach wymaga uprawnień administratora).
/// Każde zapytanie musi mieć klucz ?k=… — bez niego nikt w sieci nie odpali dźwięków.
/// </summary>
public sealed class PhoneServer : IDisposable
{
    readonly Func<string> _state;                       // JSON stanu (wywoływane z wątku tła)
    readonly Action<string, string> _action;            // (polecenie, argument) — przerzucane na wątek UI
    TcpListener? _listener;
    CancellationTokenSource? _cts;

    public int Port { get; private set; }
    public string Key { get; private set; } = "";
    public string? Error { get; private set; }
    public bool Running => _listener != null;

    public PhoneServer(Func<string> state, Action<string, string> action)
    {
        _state = state;
        _action = action;
    }

    public bool Start(int port, string key)
    {
        Stop();
        Port = port;
        Key = key;
        try
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => Loop(_listener, _cts.Token));
            Error = null;
            return true;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            _listener = null;
            return false;
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        _listener = null;
    }

    /// <summary>Adresy IPv4 komputera w sieci lokalnej (Wi-Fi/Ethernet), najlepszy pierwszy.</summary>
    public static List<string> LocalAddresses()
    {
        var list = new List<(string ip, int score)>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            var props = ni.GetIPProperties();
            bool gateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
            string name = (ni.Name + " " + ni.Description).ToLowerInvariant();
            bool virt = name.Contains("virtual") || name.Contains("vethernet") || name.Contains("vmware") || name.Contains("hyper-v") || name.Contains("vpn") || name.Contains("tailscale") || name.Contains("zerotier");
            foreach (var a in props.UnicastAddresses)
            {
                if (a.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var ip = a.Address.ToString();
                if (ip.StartsWith("169.254.")) continue;
                bool privateNet = ip.StartsWith("192.168.") || ip.StartsWith("10.") || (ip.StartsWith("172.") && int.TryParse(ip.Split('.')[1], out var b) && b is >= 16 and <= 31);
                list.Add((ip, (gateway ? 4 : 0) + (privateNet ? 2 : 0) + (virt ? -5 : 0)));
            }
        }
        return list.OrderByDescending(x => x.score).Select(x => x.ip).Distinct().ToList();
    }

    async Task Loop(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct); }
            catch { return; }
            _ = Task.Run(() => Handle(client));
        }
    }

    async Task Handle(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 5000;
                var stream = client.GetStream();
                var buf = new byte[8192];
                int n = await stream.ReadAsync(buf).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                if (n <= 0) return;
                var head = Encoding.ASCII.GetString(buf, 0, n);
                var first = head.Split("\r\n")[0].Split(' ');
                if (first.Length < 2) return;
                string method = first[0], target = first[1];
                var uri = new Uri("http://x" + target);
                var q = System.Web.HttpUtility.ParseQueryString(uri.Query);
                string path = uri.AbsolutePath;

                if (q["k"] != Key)
                {
                    await Respond(stream, 403, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(L.T("403 — zły lub brakujący klucz. Zeskanuj kod QR z ustawień TS6 Overlay.")));
                    return;
                }
                switch (path)
                {
                    case "/":
                        await Respond(stream, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(Page()));
                        break;
                    case "/api/state":
                        await Respond(stream, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(_state()));
                        break;
                    case "/api/bind" when method == "POST":
                    case "/api/stop" when method == "POST":
                    case "/api/toggle" when method == "POST":
                        _action(path[5..], q["id"] ?? "");
                        await Respond(stream, 200, "application/json", "{\"ok\":true}"u8.ToArray());
                        break;
                    case "/manifest.json":
                        await Respond(stream, 200, "application/manifest+json", Encoding.UTF8.GetBytes(Manifest()));
                        break;
                    default:
                        await Respond(stream, 404, "text/plain", "404"u8.ToArray());
                        break;
                }
            }
            catch { }
        }
    }

    static async Task Respond(NetworkStream s, int code, string type, byte[] body)
    {
        var status = code switch { 200 => "OK", 403 => "Forbidden", _ => "Not Found" };
        var header = $"HTTP/1.1 {code} {status}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
        await s.WriteAsync(Encoding.ASCII.GetBytes(header));
        await s.WriteAsync(body);
    }

    string Manifest() => JsonSerializer.Serialize(new
    {
        name = "TS6 Overlay",
        short_name = "TS6",
        start_url = "/?k=" + Key,
        display = "standalone",
        background_color = "#0b0f0c",
        theme_color = "#0b0f0c",
    });

    string Page() => PageTemplate.Replace("%LANG%", L.En ? "en" : "pl").Replace("%T%", JsonSerializer.Serialize(new
    {
        title = "TS6 Overlay",
        binds = L.T("Bindy"),
        stop = L.T("■ Zatrzymaj wszystkie dźwięki"),
        toggle = L.T("Pokaż/ukryj nakładkę"),
        server = L.T("Serwer"),
        messages = L.T("Wiadomości"),
        noBinds = L.T("Brak bindów z dźwiękiem — dodaj je w ustawieniach na komputerze."),
        offline = L.T("Brak połączenia z komputerem…"),
        notConnected = L.T("TeamSpeak: brak połączenia"),
        noMessages = L.T("Brak wiadomości w tej sesji."),
        me = L.T(" (ty)"),
    }));

    const string PageTemplate = """
<!doctype html>
<html lang="%LANG%"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
<meta name="theme-color" content="#0b0f0c"><meta name="apple-mobile-web-app-capable" content="yes">
<link rel="manifest" href="manifest.json" id="mf">
<title>TS6 Overlay</title>
<style>
  :root{--bg:#0b0f0c;--card:#121a14;--line:#1f8f3a;--fg:#7cff9e;--dim:#2fbf55;--red:#ff5555;--whisper:#e6d35a}
  *{box-sizing:border-box;-webkit-tap-highlight-color:transparent}
  body{margin:0;background:var(--bg);color:var(--fg);font:15px Consolas,ui-monospace,monospace;padding:12px 12px calc(12px + env(safe-area-inset-bottom))}
  h2{font-size:13px;color:var(--dim);margin:14px 0 6px;text-transform:uppercase;letter-spacing:.06em}
  .card{background:var(--card);border:1px solid var(--line);padding:10px;margin-bottom:10px}
  .row{display:flex;align-items:center;gap:8px;padding:3px 0}
  .dot{width:10px;height:10px;background:#145a26;flex:none}
  .talk .dot{background:var(--fg);box-shadow:0 0 8px var(--fg)} .talk{font-weight:bold}
  .whisper .dot{background:var(--whisper);box-shadow:0 0 8px var(--whisper)}
  .muted{color:var(--red);font-size:12px}
  .grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(140px,1fr));gap:8px}
  button{font:inherit;color:var(--fg);background:var(--card);border:1px solid var(--line);padding:16px 8px;min-height:64px;cursor:pointer;word-break:break-word}
  button:active{background:#1f8f3a;color:#000}
  button.playing{background:#1f8f3a;color:#000}
  .key{display:block;font-size:11px;color:var(--dim);margin-top:4px}
  .stop{width:100%;border-color:var(--red);color:var(--red);margin-top:8px}
  .stop:active{background:var(--red);color:#000}
  .tabs{display:flex;gap:6px}.tabs button{flex:1;min-height:40px;padding:8px}
  .tabs button.on{background:var(--line);color:#000}
  .hdr{font-weight:bold;color:var(--dim);margin:6px 0 2px} .msg{padding:4px 0;border-bottom:1px solid #18301e}
  .time{color:var(--dim);font-size:12px;margin-right:6px}
  #off{display:none;color:var(--red);text-align:center;padding:8px}
</style></head>
<body>
<div id="off"></div>
<div class="card" id="chan"></div>
<h2 id="bindsH"></h2>
<div class="grid" id="binds"></div>
<button class="stop" id="stopBtn" onclick="post('stop')"></button>
<button class="stop" style="border-color:var(--line);color:var(--fg)" id="toggleBtn" onclick="post('toggle')"></button>
<div class="tabs" style="margin-top:14px"><button id="tS" class="on" onclick="tab('s')"></button><button id="tM" onclick="tab('m')"></button></div>
<div class="card" id="tree" style="margin-top:8px"></div>
<script>
const T = %T%;
const k = new URLSearchParams(location.search).get('k');
document.getElementById('mf').href = 'manifest.json?k=' + encodeURIComponent(k);
const $ = id => document.getElementById(id);
$('bindsH').textContent = T.binds; $('stopBtn').textContent = T.stop; $('toggleBtn').textContent = T.toggle;
$('tS').textContent = T.server; $('tM').textContent = T.messages; $('off').textContent = T.offline;
const esc = s => String(s).replace(/[&<>"]/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]));
let view = 's', lastBinds = '';
function tab(v){ view = v; $('tS').className = v==='s'?'on':''; $('tM').className = v==='m'?'on':''; tick(); }
async function post(cmd, id){ try{ await fetch('api/' + cmd + '?k=' + encodeURIComponent(k) + (id ? '&id=' + encodeURIComponent(id) : ''), {method:'POST'}); if (navigator.vibrate) navigator.vibrate(30); }catch(e){} }
const row = m => '<div class="row ' + (m.talking ? (m.whisper ? 'talk whisper' : 'talk') : '') + '"><span class="dot"></span><span>'
  + (m.fav ? '★ ' : '') + esc(m.nick) + (m.me ? T.me : '') + '</span>' + (m.muted ? '<span class="muted">🎙✕</span>' : '') + (m.deaf ? '<span class="muted">🔇</span>' : '') + '</div>';
async function tick(){
  try{
    const s = await (await fetch('api/state?k=' + encodeURIComponent(k), {cache:'no-store'})).json();
    $('off').style.display = 'none';
    // kanał(y)
    let h = '';
    if (!s.servers.length) h = esc(T.notConnected);
    for (const v of s.servers){
      if (s.servers.length > 1) h += '<div class="hdr">🖧 ' + esc(v.server) + '</div>';
      h += '<div class="hdr">🔊 ' + esc(v.channel) + '</div>' + v.members.map(row).join('');
      for (const w of v.watched) h += '<div class="hdr">👁 ' + esc(w.name) + ' (' + w.members.length + ')</div>' + w.members.map(row).join('');
    }
    $('chan').innerHTML = h;
    // bindy (przebudowa tylko przy zmianie)
    const bj = JSON.stringify(s.binds) + s.playing;
    if (bj !== lastBinds){
      lastBinds = bj;
      $('binds').innerHTML = s.binds.length ? s.binds.map(b => '<button class="' + (b.id === s.playing ? 'playing' : '') + '" onclick="post(\'bind\',\'' + b.id + '\')">' + esc(b.name) + (b.key ? '<span class="key">' + esc(b.key) + '</span>' : '') + '</button>').join('')
        : '<div style="grid-column:1/-1;color:var(--dim)">' + esc(T.noBinds) + '</div>';
    }
    // serwer albo wiadomości
    if (view === 's'){
      $('tree').innerHTML = s.tree.map(c => '<div class="hdr">' + (c.mine ? '🔊 ' : '') + esc(c.name) + ' (' + c.members.length + ')</div>' + c.members.map(row).join('')).join('') || '—';
    } else {
      $('tree').innerHTML = s.messages.length ? s.messages.map(m => '<div class="msg"><span class="time">' + esc(m.time) + '</span>' + esc(m.text) + '</div>').join('') : esc(T.noMessages);
    }
  }catch(e){ $('off').style.display = 'block'; }
}
setInterval(tick, 700); tick();
</script></body></html>
""";

    public void Dispose() => Stop();
}
