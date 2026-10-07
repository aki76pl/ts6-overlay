using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static TS6Overlay.L;

namespace TS6Overlay;

/// <summary>
/// Status w Discordzie (Rich Presence) przez lokalny kanał \\.\pipe\discord-ipc-N.
/// Wymaga identyfikatora aplikacji z discord.com/developers (Application ID) — jej nazwa wyświetla się jako „Gra w …”.
/// Discord pozwala na ~5 zmian statusu na 20 s, więc wysyłamy najwyżej co 15 s (ostatni stan wygrywa).
/// </summary>
public sealed class DiscordPresence : IDisposable
{
    readonly Config _cfg;
    NamedPipeClientStream? _pipe;
    string _connectedId = "";
    string? _pending, _lastSent;
    DateTime _lastSend = DateTime.MinValue, _since = DateTime.UtcNow, _lastAttempt = DateTime.MinValue;
    string _sinceKey = "";
    readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };

    public string Status { get; private set; } = "";

    public DiscordPresence(Config cfg)
    {
        _cfg = cfg;
        _timer.Tick += (_, _) => Flush();
        _timer.Start();
    }

    /// <summary>Wywoływane przy każdej zmianie stanu TS (wątek UI).</summary>
    public void Update(OverlayState s)
    {
        if (!_cfg.DiscordEnabled || _cfg.DiscordAppId.Trim() == "") { Clear(); return; }
        var a = s.Active;
        if (a == null) { _pending = "clear"; return; }

        // Czas liczony od wejścia na bieżący kanał.
        var key = a.ConnectionId + "/" + a.Channel;
        if (key != _sinceKey) { _sinceKey = key; _since = DateTime.UtcNow; }

        string channel = a.Channel == "" ? T("kanał") : a.Channel;
        string details = _cfg.DiscordShowChannel ? T("Na TeamSpeaku: {0}", channel) : T("Na TeamSpeaku");
        string state = a.Members.Count <= 1 ? T("sam(a) na kanale") : T("{0} osób na kanale", a.Members.Count);
        if (_cfg.DiscordShowServer && a.Server != "") state += " · " + a.Server;

        var activity = new JsonObject
        {
            ["details"] = Trim(details),
            ["state"] = Trim(state),
            ["timestamps"] = new JsonObject { ["start"] = new DateTimeOffset(_since).ToUnixTimeSeconds() },
            ["instance"] = false,
        };
        if (_cfg.DiscordLargeImage.Trim() != "")
            activity["assets"] = new JsonObject { ["large_image"] = _cfg.DiscordLargeImage.Trim(), ["large_text"] = "TS6 Overlay" };
        _pending = activity.ToJsonString();
    }

    static string Trim(string s) => s.Length > 120 ? s[..120] : s.Length < 2 ? s + "  " : s;

    void Clear()
    {
        if (_pipe != null) { _pending = "clear"; Flush(); Disconnect(); }
        Status = _cfg.DiscordEnabled ? T("Podaj Application ID.") : T("Wyłączone.");
    }

    void Flush()
    {
        if (_pending == null || !_cfg.DiscordEnabled) return;
        if (_pending == _lastSent) { _pending = null; return; }
        if ((DateTime.UtcNow - _lastSend).TotalSeconds < 15 && _lastSent != null) return;
        if (!EnsureConnected()) return;
        try
        {
            var args = new JsonObject { ["pid"] = Environment.ProcessId };
            if (_pending != "clear") args["activity"] = JsonNode.Parse(_pending);
            Send(1, new JsonObject { ["cmd"] = "SET_ACTIVITY", ["args"] = args, ["nonce"] = Guid.NewGuid().ToString() });
            Read();   // odpowiedź Discorda (błąd = zły identyfikator itp.)
            _lastSent = _pending;
            _lastSend = DateTime.UtcNow;
            _pending = null;
            Status = T("Połączono z Discordem — status jest widoczny w Twoim profilu.");
        }
        catch (Exception ex)
        {
            Status = T("Błąd Discorda: {0}", ex.Message);
            Disconnect();
        }
    }

    bool EnsureConnected()
    {
        var id = _cfg.DiscordAppId.Trim();
        if (_pipe is { IsConnected: true } && _connectedId == id) return true;
        Disconnect();
        if ((DateTime.UtcNow - _lastAttempt).TotalSeconds < 15) return false;
        _lastAttempt = DateTime.UtcNow;
        for (int i = 0; i < 10; i++)
        {
            try
            {
                var p = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.None);
                p.Connect(300);
                _pipe = p;
                Send(0, new JsonObject { ["v"] = 1, ["client_id"] = id });
                var ready = Read();
                if (ready?["evt"]?.GetValue<string>() != "READY")
                {
                    Status = T("Discord odrzucił połączenie — sprawdź Application ID.");
                    Disconnect();
                    return false;
                }
                _connectedId = id;
                _lastSent = null;
                return true;
            }
            catch (TimeoutException) { _pipe?.Dispose(); _pipe = null; }
            catch (Exception ex) { Status = T("Błąd Discorda: {0}", ex.Message); Disconnect(); return false; }
        }
        Status = T("Nie znaleziono uruchomionego Discorda (aplikacji na komputerze).");
        return false;
    }

    void Send(int op, JsonNode payload)
    {
        var body = Encoding.UTF8.GetBytes(payload.ToJsonString());
        var header = new byte[8];
        BitConverter.GetBytes(op).CopyTo(header, 0);
        BitConverter.GetBytes(body.Length).CopyTo(header, 4);
        _pipe!.Write(header);
        _pipe.Write(body);
        _pipe.Flush();
    }

    JsonNode? Read()
    {
        var header = new byte[8];
        ReadExact(header);
        int len = BitConverter.ToInt32(header, 4);
        if (len <= 0 || len > 1 << 20) return null;
        var body = new byte[len];
        ReadExact(body);
        var node = JsonNode.Parse(body);
        if (node?["evt"]?.GetValue<string>() == "ERROR")
            throw new IOException(node["data"]?["message"]?.GetValue<string>() ?? "error");
        return node;
    }

    void ReadExact(byte[] buf)
    {
        var task = Task.Run(() =>
        {
            int read = 0;
            while (read < buf.Length)
            {
                int n = _pipe!.Read(buf, read, buf.Length - read);
                if (n == 0) throw new EndOfStreamException();
                read += n;
            }
        });
        if (!task.Wait(3000)) throw new TimeoutException(T("Discord nie odpowiada."));
        if (task.Exception != null) throw task.Exception.InnerException!;
    }

    void Disconnect()
    {
        try { _pipe?.Dispose(); } catch { }
        _pipe = null;
        _connectedId = "";
    }

    public void Dispose()
    {
        _timer.Stop();
        Disconnect();
    }
}
