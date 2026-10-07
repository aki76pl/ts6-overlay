using System.Net;
using System.Text;
using System.Text.Json;

namespace TS6Overlay;

/// <summary>
/// Strona dla OBS (źródło „Przeglądarka”): http://localhost:PORT/
/// Parametry: ?only=talking (tylko mówiący), ?scale=1.5, ?notices=0 (bez powiadomień).
/// </summary>
public sealed class ObsServer : IDisposable
{
    HttpListener? _http;
    volatile string _stateJson = "{}";
    readonly List<object> _notices = new();
    int _noticeId;

    public int Port { get; private set; }
    public string Url => $"http://localhost:{Port}/";
    public string? Error { get; private set; }

    public bool Start(int port)
    {
        Stop();
        Port = port;
        try
        {
            _http = new HttpListener();
            _http.Prefixes.Add($"http://localhost:{port}/");
            _http.Prefixes.Add($"http://127.0.0.1:{port}/");
            _http.Start();
            _ = Task.Run(Loop);
            Error = null;
            return true;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            _http = null;
            return false;
        }
    }

    public void Stop()
    {
        try { _http?.Stop(); _http?.Close(); } catch { }
        _http = null;
    }

    /// <summary>Wywoływane z wątku UI przy każdej zmianie.</summary>
    public void Update(OverlayState s, Theme t, Config cfg)
    {
        object Member(ClientInfo m, int myId) => new
        {
            nick = m.Nickname,
            talking = m.Talking,
            whisper = m.Whisper,
            me = m.Id == myId,
            muted = m.InputMuted,
            deaf = m.OutputMuted,
            color = cfg.FavoriteFor(m.Uid)?.Color,
        };
        var servers = s.Servers.Select(v => new
        {
            server = v.Server,
            channel = v.Channel,
            members = v.Members.Select(m => Member(m, v.MyId)),
            watched = v.Watched.Select(w => new { name = w.Name, members = w.Members.Select(m => Member(m, 0)) }),
        });
        object[] notices;
        lock (_notices) notices = _notices.ToArray();
        _stateJson = JsonSerializer.Serialize(new
        {
            connected = s.Servers.Count > 0,
            servers,
            texts = new { disconnected = L.T("TeamSpeak: brak połączenia"), me = L.T(" (ty)") },
            notices,
            noticeSeconds = cfg.EventSeconds,
            messageSeconds = cfg.MessageSeconds,
            theme = Theme.ColorFields.ToDictionary(f => f.Prop, f => CssColor(t.GetColor(f.Prop))),
            font = t.Font,
            radius = t.Radius,
            borderWidth = t.BorderWidth,
            square = t.SquareDots,
            shadow = t.TextShadow,
            glow = t.Glow,
        });
    }

    public void AddNotice(Notice n)
    {
        lock (_notices)
        {
            _notices.Add(new { id = ++_noticeId, text = n.Text, kind = n.Kind.ToString(), at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
            while (_notices.Count > 8) _notices.RemoveAt(0);
        }
    }

    /// <summary>#AARRGGBB → rgba() dla CSS.</summary>
    static string CssColor(string hex)
    {
        var c = Theme.C(hex);
        return $"rgba({c.R},{c.G},{c.B},{(c.A / 255.0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)})";
    }

    async Task Loop()
    {
        var http = _http;
        while (http != null && http.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await http.GetContextAsync(); }
            catch { return; }
            try
            {
                string path = ctx.Request.Url?.AbsolutePath ?? "/";
                byte[] body;
                if (path == "/state")
                {
                    body = Encoding.UTF8.GetBytes(_stateJson);
                    ctx.Response.ContentType = "application/json; charset=utf-8";
                    ctx.Response.Headers["Cache-Control"] = "no-store";
                }
                else if (path == "/")
                {
                    body = Encoding.UTF8.GetBytes(Page);
                    ctx.Response.ContentType = "text/html; charset=utf-8";
                }
                else
                {
                    ctx.Response.StatusCode = 404;
                    body = Array.Empty<byte>();
                }
                ctx.Response.ContentLength64 = body.Length;
                await ctx.Response.OutputStream.WriteAsync(body);
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }
    }

    public void Dispose() => Stop();

    const string Page = """
<!doctype html>
<html lang="pl"><head><meta charset="utf-8"><title>TS6 Overlay — OBS</title>
<style>
  html,body{margin:0;background:transparent;overflow:hidden}
  #root{display:inline-block;padding:8px 10px;min-width:180px;transform-origin:0 0}
  .hdr{font-size:12px;font-weight:600;margin-bottom:4px}
  .row{display:flex;align-items:center;gap:7px;font-size:14px;padding:1px 4px;margin:0 -4px}
  .dot{width:10px;height:10px;border-radius:50%;flex:none}
  .sq .dot{border-radius:0}
  .ic{font-size:11px}
  .n{border-radius:5px;padding:4px 8px;margin:4px 0 0;font-size:13px;transition:opacity .6s}
  .gone{opacity:0}
</style></head>
<body><div id="root"></div>
<script>
const q = new URLSearchParams(location.search);
const onlyTalking = q.get('only') === 'talking';
const showNotices = q.get('notices') !== '0';
const scale = parseFloat(q.get('scale') || '1');
const root = document.getElementById('root');
root.style.transform = 'scale(' + scale + ')';
const esc = s => s.replace(/[&<>"]/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]));
async function tick() {
  try {
    const s = await (await fetch('/state', {cache: 'no-store'})).json();
    const t = s.theme || {};
    root.style.background = t.Panel; root.style.border = s.borderWidth + 'px solid ' + t.PanelBorder;
    root.style.borderRadius = s.radius + 'px'; root.style.fontFamily = "'" + s.font + "', 'Segoe UI', sans-serif";
    root.className = s.square ? 'sq' : '';
    const shadow = s.shadow ? 'text-shadow:0 1px 4px #000;' : '';
    let h = '';
    const row = (m, small) => {
        const dc = m.talking ? (m.whisper ? t.Whisper : t.Talk) : t.Idle;
        const glow = m.talking && s.glow ? 'box-shadow:0 0 8px ' + dc + ';' : '';
        const nc = m.color || (m.talking ? t.TextTalking : t.Text);
        return '<div class="row" style="background:' + (m.talking ? t.TalkRow : 'transparent') + (small ? ';font-size:12px' : '') + '">'
          + '<span class="dot" style="background:' + dc + ';' + glow + '"></span>'
          + '<span style="color:' + nc + ';font-weight:' + (m.talking ? 700 : 400) + ';' + shadow + '">' + (m.color ? '★ ' : '') + esc(m.nick) + (m.me ? s.texts.me : '') + '</span>'
          + (m.muted ? '<span class="ic" style="color:' + t.Muted + '">🎙✕</span>' : '')
          + (m.deaf ? '<span class="ic" style="color:' + t.Muted + '">🔇</span>' : '')
          + '</div>';
    };
    if (!s.connected) h = '<div class="hdr" style="color:' + t.Header + '">' + esc(s.texts.disconnected) + '</div>';
    else {
      const multi = s.servers.length > 1;
      for (const v of s.servers) {
        if (multi) h += '<div class="hdr" style="color:' + t.Header + ';opacity:.75;font-size:11px;margin-top:6px">🖧 ' + esc(v.server) + '</div>';
        h += '<div class="hdr" style="color:' + t.Header + ';' + shadow + '">🔊 ' + esc(v.channel) + '</div>';
        for (const m of v.members) if (!onlyTalking || m.talking) h += row(m, false);
        for (const w of v.watched) {
          h += '<div class="hdr" style="color:' + t.Header + ';font-size:11px;margin-top:6px">👁 ' + esc(w.name) + ' (' + w.members.length + ')</div>';
          for (const m of w.members) if (!onlyTalking || m.talking) h += row(m, true);
        }
      }
      if (showNotices) {
        const now = Date.now();
        for (const n of s.notices) {
          const life = ((n.kind === 'Message' || n.kind === 'Poke') ? s.messageSeconds : s.noticeSeconds) * 1000;
          const age = now - n.at;
          if (age > life + 600) continue;
          const bg = {Join: t.Join, Leave: t.Leave, Message: t.Message, Poke: t.Poke, Friend: t.Join}[n.kind] || t.Info;
          const pre = {Join: '➜ ', Leave: '← ', Message: '✉ ', Poke: '👉 '}[n.kind] || '• ';
          h += '<div class="n' + (age > life ? ' gone' : '') + '" style="background:' + bg + ';color:' + t.NoticeText + '">' + pre + esc(n.text) + '</div>';
        }
      }
    }
    root.innerHTML = h;
  } catch (e) { root.innerHTML = ''; }
}
setInterval(tick, 250); tick();
</script></body></html>
""";
}
