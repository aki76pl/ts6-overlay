using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace TS6Overlay;

public sealed class ClientInfo
{
    public int Id;
    public string Uid = "";
    public string Nickname = "";
    public int ChannelId;
    public bool Talking;
    public bool Whisper;
    public bool InputMuted;
    public bool OutputMuted;

    public ClientInfo Copy() => (ClientInfo)MemberwiseClone();
}

public sealed class ConnectionState
{
    public int Id;
    public int MyClientId;
    public bool Connected;
    public string ServerName = "";
    public readonly Dictionary<int, ClientInfo> Clients = new();
    public readonly Dictionary<int, string> Channels = new();

    public int MyChannelId => Clients.TryGetValue(MyClientId, out var me) ? me.ChannelId : 0;
    public string ChannelName(int id) => Channels.TryGetValue(id, out var n) ? CleanChannelName(n) : $"kanał #{id}";

    // Znacznik spacera TS: [spacer], [cspacer], [lspacer0], [*spacer] itd.
    static readonly Regex SpacerTag = new(@"^\s*\[[lcr*]?spacer[^\]]*\]", RegexOptions.IgnoreCase);

    /// <summary>„[spacer]╟-● Lobby” → „Lobby”: bez znacznika spacera i ozdobników na brzegach.</summary>
    public static string CleanChannelName(string name)
    {
        var s = SpacerTag.Replace(name, "");
        int a = 0, b = s.Length;
        while (a < b && IsDecor(s[a])) a++;
        while (b > a && IsDecor(s[b - 1])) b--;
        return s[a..b];
    }

    static bool IsDecor(char ch) =>
        char.IsWhiteSpace(ch)
        || (ch >= '─' && ch <= '◿')   // ramki ╟│═, bloki ░▓, kształty ●■▶
        || (ch >= '‐' && ch <= '‧')   // myślniki –—, kropki •‥…
        || (ch >= '←' && ch <= '⇿')   // strzałki
        || (ch >= '☀' && ch <= '➿')   // gwiazdki ★, dingbaty ✦➤
        || "-_=~|·*+#.:<>»«¦¤°".IndexOf(ch) >= 0;

    public ClientInfo Get(int clientId)
    {
        if (!Clients.TryGetValue(clientId, out var c))
            Clients[clientId] = c = new ClientInfo { Id = clientId, Nickname = $"Klient #{clientId}" };
        return c;
    }
}

public enum NoticeKind { Join, Leave, Info, Message, Poke }

public sealed record Notice(string Text, NoticeKind Kind, ClientInfo? Who = null);

/// <summary>Stan do narysowania: kanał, osoby na nim, Twoje wyciszenia.</summary>
public sealed record OverlayState(
    string? Channel,               // null = brak połączenia; "" = kanał bez nazwy (sam spacer)
    List<ClientInfo> Members,
    int MyId,
    bool MyInputMuted,
    bool MyOutputMuted,
    string Server)
{
    public static readonly OverlayState Disconnected = new(null, new(), 0, false, false, "");
}

/// <summary>
/// Klient lokalnego API „Remote Apps” TeamSpeak 5/6 (WebSocket ws://127.0.0.1:5899).
/// Wszystkie zdarzenia są wywoływane z wątku tła — UI musi je przerzucić na Dispatcher.
/// </summary>
public sealed class TsClient
{
    const string Url = "ws://127.0.0.1:5899";
    readonly Config _cfg;
    readonly Dictionary<int, ConnectionState> _conns = new();
    readonly object _lock = new();
    readonly HashSet<string> _unknownTypes = new();
    int _currentConnectionId;

    public event Action? StateChanged;
    public event Action<Notice>? Notice;
    /// <summary>Ktoś zaczął/przestał mówić (do statystyk).</summary>
    public event Action<ClientInfo, bool>? TalkChanged;
    public event Action<string>? Status;

    public TsClient(Config cfg) => _cfg = cfg;

    public OverlayState Snapshot()
    {
        lock (_lock)
        {
            var c = Active();
            if (c == null || !c.Connected) return OverlayState.Disconnected;
            int ch = c.MyChannelId;
            var list = c.Clients.Values
                .Where(x => x.ChannelId == ch && ch != 0 && (x.Id == c.MyClientId || !_cfg.IsIgnored(x.Uid)))
                .Select(x => x.Copy())
                .OrderBy(x => x.Nickname, StringComparer.CurrentCultureIgnoreCase).ToList();
            var me = c.Clients.GetValueOrDefault(c.MyClientId);
            return new OverlayState(c.ChannelName(ch), list, c.MyClientId,
                me?.InputMuted ?? false, me?.OutputMuted ?? false, c.ServerName);
        }
    }

    /// <summary>Wszyscy widoczni na serwerze (do listy ulubionych/ignorowanych).</summary>
    public List<ClientInfo> AllClients()
    {
        lock (_lock)
        {
            var c = Active();
            if (c == null) return new();
            return c.Clients.Values.Where(x => x.Id != c.MyClientId && x.Uid != "")
                .Select(x => x.Copy()).OrderBy(x => x.Nickname, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }

    ConnectionState? Active()
    {
        if (_conns.TryGetValue(_currentConnectionId, out var c) && c.Connected) return c;
        return _conns.Values.FirstOrDefault(x => x.Connected) ?? _conns.GetValueOrDefault(_currentConnectionId);
    }

    ConnectionState Conn(int id)
    {
        if (!_conns.TryGetValue(id, out var c)) _conns[id] = c = new ConnectionState { Id = id };
        return c;
    }

    void Raise(Notice n)
    {
        if (n.Who != null && _cfg.IsIgnored(n.Who.Uid)) return;
        Notice?.Invoke(n);
    }

    public async Task RunAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();
                Status?.Invoke("Łączenie z TeamSpeak…");
                await ws.ConnectAsync(new Uri(Url), stop);
                await SendAsync(ws, new JsonObject
                {
                    ["type"] = "auth",
                    ["payload"] = new JsonObject
                    {
                        ["identifier"] = "pl.artur.ts6overlay",
                        ["version"] = "1.0.0",
                        ["name"] = "TS6 Overlay",
                        ["description"] = "Nakładka w grze: kto wchodzi na kanał i kto mówi",
                        ["content"] = new JsonObject { ["apiKey"] = _cfg.ApiKey }
                    }
                }, stop);
                Status?.Invoke("Czekam na zgodę w TeamSpeak (Ustawienia → Remote Apps)…");
                await ReceiveLoop(ws, stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                Status?.Invoke("Brak połączenia z TeamSpeak — ponawiam… (" + ex.Message + ")");
            }
            lock (_lock) { _conns.Clear(); }
            StateChanged?.Invoke();
            try { await Task.Delay(3000, stop); } catch { return; }
        }
    }

    static Task SendAsync(ClientWebSocket ws, JsonNode msg, CancellationToken ct) =>
        ws.SendAsync(Encoding.UTF8.GetBytes(msg.ToJsonString()), WebSocketMessageType.Text, true, ct);

    async Task ReceiveLoop(ClientWebSocket ws, CancellationToken ct)
    {
        var buf = new byte[1 << 20];
        var ms = new MemoryStream();
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var r = await ws.ReceiveAsync(buf, ct);
            if (r.MessageType == WebSocketMessageType.Close) break;
            ms.Write(buf, 0, r.Count);
            if (!r.EndOfMessage) continue;
            var text = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
            ms.SetLength(0);
            if (_cfg.LogRawEvents)
                try { File.AppendAllText(Config.LogPath, DateTime.Now.ToString("HH:mm:ss ") + text + Environment.NewLine); } catch { }
            try { Handle(JsonNode.Parse(text), text); }
            catch (Exception ex) { Status?.Invoke("Błąd zdarzenia: " + ex.Message); }
        }
    }

    // ---------- obsługa zdarzeń ----------

    void Handle(JsonNode? msg, string raw)
    {
        if (msg == null) return;
        string type = Str(msg["type"]);
        var p = msg["payload"];
        if (p == null) return;
        bool changed = true;

        lock (_lock)
        {
            switch (type)
            {
                case "auth": OnAuth(p); break;
                case "connectStatusChanged": OnConnectStatus(p); break;
                case "channels": OnChannels(Conn(Int(p["connectionId"])), p["info"] ?? p); break;
                case "channelPropertiesUpdated":
                case "channelEdited":
                    {
                        var c = Conn(Int(p["connectionId"]));
                        int id = Int(p["channelId"]);
                        var name = Str(p["properties"]?["name"]);
                        if (id != 0 && name != "") c.Channels[id] = name;
                        break;
                    }
                case "clientMoved": OnClientMoved(p); break;
                case "talkStatusChanged":
                    {
                        var c = Conn(Int(p["connectionId"]));
                        var cl = c.Get(Int(p["clientId"]));
                        bool talking = Int(p["status"]) == 1;
                        cl.Whisper = Bool(p["isWhisper"]);
                        if (talking != cl.Talking)
                        {
                            cl.Talking = talking;
                            if (!_cfg.IsIgnored(cl.Uid)) TalkChanged?.Invoke(cl.Copy(), talking);
                        }
                        break;
                    }
                case "clientPropertiesUpdated":
                    {
                        var c = Conn(Int(p["connectionId"]));
                        ApplyClientProps(c.Get(Int(p["clientId"])), p["properties"]);
                        break;
                    }
                case "clientSelfPropertyUpdated":
                    {
                        var c = Conn(Int(p["connectionId"]));
                        string flag = Str(p["flag"]);
                        var me = c.Get(c.MyClientId);
                        if (flag == "inputMuted") me.InputMuted = Bool(p["newValue"]);
                        else if (flag == "outputMuted") me.OutputMuted = Bool(p["newValue"]);
                        else if (flag == "nickname") me.Nickname = Str(p["newValue"]);
                        else changed = false;
                        break;
                    }
                default:
                    if (type.Contains("poke", StringComparison.OrdinalIgnoreCase)) OnPoke(p);
                    else if (type.Contains("message", StringComparison.OrdinalIgnoreCase)
                             && !type.Contains("log", StringComparison.OrdinalIgnoreCase)) OnTextMessage(p);
                    else LogUnknown(type, raw);
                    changed = false;
                    break;
            }
        }
        if (changed) StateChanged?.Invoke();
    }

    /// <summary>Nieznane typy zdarzeń zapisujemy raz — ułatwia dopasowanie do nowych wersji TS.</summary>
    void LogUnknown(string type, string raw)
    {
        if (!_unknownTypes.Add(type)) return;
        try
        {
            Directory.CreateDirectory(Config.Dir);
            File.AppendAllText(Path.Combine(Config.Dir, "unknown-events.log"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + (raw.Length > 3000 ? raw[..3000] : raw) + Environment.NewLine);
        }
        catch { }
    }

    void OnAuth(JsonNode p)
    {
        var key = Str(p["apiKey"]);
        if (key != "" && key != _cfg.ApiKey) { _cfg.ApiKey = key; _cfg.Save(); }
        _currentConnectionId = Int(p["currentConnectionId"]);
        _conns.Clear();
        if (p["connections"] is JsonArray arr)
            foreach (var cn in arr)
            {
                if (cn == null) continue;
                var c = Conn(Int(cn["id"]));
                c.MyClientId = Int(cn["clientId"]);
                c.Connected = c.MyClientId != 0 || Int(cn["status"]) >= 3;
                c.ServerName = Str(cn["properties"]?["name"]);
                OnChannels(c, cn["channelInfos"]);
                if (cn["clientInfos"] is JsonArray clients)
                    foreach (var ci in clients)
                    {
                        if (ci == null) continue;
                        var cl = c.Get(Int(ci["id"]));
                        cl.ChannelId = Int(ci["channelId"]);
                        ApplyClientProps(cl, ci["properties"]);
                    }
            }
        Status?.Invoke("Połączono z TeamSpeak");
    }

    void OnConnectStatus(JsonNode p)
    {
        var c = Conn(Int(p["connectionId"]));
        int status = Int(p["status"]);
        // 0 = rozłączony, 4 = połączony (kolejne etapy 1–3 to łączenie)
        if (status == 0)
        {
            c.Connected = false;
            c.Clients.Clear();
            c.Channels.Clear();
        }
        else if (status >= 3)
        {
            c.Connected = true;
            int cid = Int(p["info"]?["clientId"]);
            if (cid != 0) c.MyClientId = cid;
            var server = Str(p["info"]?["serverName"]);
            if (server != "") c.ServerName = server;
            _currentConnectionId = c.Id;
        }
    }

    void OnChannels(ConnectionState c, JsonNode? info)
    {
        if (info == null) return;
        void Add(JsonNode? ch)
        {
            if (ch == null) return;
            int id = Int(ch["id"]);
            var name = Str(ch["properties"]?["name"]);
            if (id != 0) c.Channels[id] = name != "" ? name : Str(ch["name"]);
        }
        if (info["rootChannels"] is JsonArray roots) foreach (var ch in roots) Add(ch);
        if (info["subChannels"] is JsonObject subs)
            foreach (var kv in subs)
                if (kv.Value is JsonArray list) foreach (var ch in list) Add(ch);
    }

    void OnClientMoved(JsonNode p)
    {
        var c = Conn(Int(p["connectionId"]));
        int clientId = Int(p["clientId"]);
        int oldCh = Int(p["oldChannelId"]);
        int newCh = Int(p["newChannelId"]);
        var cl = c.Get(clientId);
        ApplyClientProps(cl, p["properties"]);

        int myCh = c.MyChannelId;
        bool isMe = clientId == c.MyClientId;
        cl.ChannelId = newCh;

        if (isMe)
        {
            if (newCh != 0 && newCh != oldCh)
                Raise(new(c.ChannelName(newCh) is { Length: > 0 } n ? $"Jesteś na kanale: {n}" : "Zmieniłeś kanał", NoticeKind.Info));
            return;
        }
        if (newCh == myCh && oldCh != myCh && myCh != 0)
            Raise(new($"{cl.Nickname} dołączył(a) do kanału", NoticeKind.Join, cl.Copy()));
        else if (oldCh == myCh && newCh != myCh && myCh != 0)
            Raise(new(newCh == 0
                ? $"{cl.Nickname} rozłączył(a) się"
                : c.ChannelName(newCh) is { Length: > 0 } n2
                    ? $"{cl.Nickname} przeszedł/przeszła na: {n2}"
                    : $"{cl.Nickname} przeszedł/przeszła na inny kanał", NoticeKind.Leave, cl.Copy()));

        if (newCh == 0)
        {
            if (cl.Talking && !_cfg.IsIgnored(cl.Uid)) TalkChanged?.Invoke(cl.Copy(), false);
            cl.Talking = false;
            c.Clients.Remove(clientId);
        }
    }

    // ---------- wiadomości i szturchnięcia ----------
    // Format zdarzeń tekstowych nie jest udokumentowany, więc szukamy pól pod kilkoma nazwami.

    static readonly Regex BbCode = new(@"\[/?[a-zA-Z*]+(=[^\]]*)?\]");

    static string FirstStr(JsonNode? p, params string[] keys)
    {
        foreach (var k in keys)
        {
            var v = Str(p?[k]);
            if (v != "") return v;
        }
        return "";
    }

    (ClientInfo? who, string name) Sender(ConnectionState c, JsonNode p)
    {
        var inv = p["invoker"] ?? p["from"] ?? p["sender"];
        int id = Int(inv?["id"]);
        if (id == 0) id = Int(inv?["clientId"]);
        if (id == 0) id = Int(p["invokerId"]);
        if (id == 0) id = Int(p["fromId"]);
        if (id == 0) id = Int(p["clientId"]);
        string name = FirstStr(inv, "nickname", "name");
        if (name == "") name = FirstStr(p, "invokerName", "fromName", "nickname", "name");
        var who = id != 0 && c.Clients.TryGetValue(id, out var known) ? known.Copy() : null;
        if (who == null)
        {
            var uid = FirstStr(inv, "uniqueIdentifier", "uid");
            if (uid == "") uid = FirstStr(p, "invokerUniqueIdentifier", "invokerUid");
            who = new ClientInfo { Id = id, Uid = uid, Nickname = name };
        }
        if (name == "") name = who.Nickname != "" ? who.Nickname : "Ktoś";
        return (who, name);
    }

    void OnTextMessage(JsonNode p)
    {
        var c = Conn(Int(p["connectionId"]));
        var (who, name) = Sender(c, p);
        if (who != null && who.Id != 0 && who.Id == c.MyClientId) return;   // własne wiadomości
        string text = BbCode.Replace(FirstStr(p, "message", "msg", "text", "content"), "").Trim();
        if (text == "") return;
        // targetMode jak w TS3: 1 = prywatna, 2 = kanał, 3 = serwer
        int mode = Int(p["targetMode"]);
        if (mode == 0) mode = Int(p["target_mode"]);
        bool show = mode switch
        {
            1 => _cfg.ShowPrivateMessages,
            3 => _cfg.ShowServerMessages,
            _ => _cfg.ShowChannelMessages,
        };
        if (!show) return;
        string where = mode switch { 1 => " (prywatnie)", 3 => " (serwer)", _ => "" };
        if (text.Length > 160) text = text[..160] + "…";
        Raise(new($"{name}{where}: {text}", NoticeKind.Message, who));
    }

    void OnPoke(JsonNode p)
    {
        if (!_cfg.ShowPokes) return;
        var c = Conn(Int(p["connectionId"]));
        var (who, name) = Sender(c, p);
        string text = BbCode.Replace(FirstStr(p, "message", "msg", "text", "pokeMessage"), "").Trim();
        Raise(new(text == "" ? $"{name} szturcha Cię!" : $"{name} szturcha Cię: {text}", NoticeKind.Poke, who));
    }

    static void ApplyClientProps(ClientInfo cl, JsonNode? props)
    {
        if (props is not JsonObject o) return;
        var nick = Str(o["nickname"]);
        if (nick != "") cl.Nickname = nick;
        var uid = Str(o["uniqueIdentifier"]);
        if (uid != "") cl.Uid = uid;
        if (o.ContainsKey("inputMuted")) cl.InputMuted = Bool(o["inputMuted"]);
        if (o.ContainsKey("outputMuted")) cl.OutputMuted = Bool(o["outputMuted"]);
    }

    // ---------- pomocnicze: API potrafi wysyłać liczby jako stringi i odwrotnie ----------

    static string Str(JsonNode? n) => n is JsonValue v
        ? (v.TryGetValue<string>(out var s) ? s : v.ToJsonString().Trim('"'))
        : "";

    static int Int(JsonNode? n)
    {
        if (n is not JsonValue v) return 0;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<long>(out var l)) return (int)l;
        if (v.TryGetValue<double>(out var d)) return (int)d;
        return int.TryParse(Str(n), out i) ? i : 0;
    }

    static bool Bool(JsonNode? n)
    {
        if (n is not JsonValue v) return false;
        if (v.TryGetValue<bool>(out var b)) return b;
        return Int(n) != 0 || Str(n).Equals("true", StringComparison.OrdinalIgnoreCase);
    }
}
