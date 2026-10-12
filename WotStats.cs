using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TS6Overlay;

/// <summary>
/// Statystyki World of Tanks osób z TS przez oficjalne API Wargaming (wymaga własnego Application ID
/// z developers.wargaming.net). Nick WoT = nick w TS albo ręcznie przypisany. Wyniki trzymane w pamięci
/// podręcznej (wot-cache.json): znalezieni 6 h, nieznalezieni 24 h.
/// </summary>
public sealed class WotStats
{
    public sealed class Entry
    {
        public long AccountId { get; set; }
        public string Nickname { get; set; } = "";
        public bool NotFound { get; set; }
        public int Battles { get; set; }
        public int Wins { get; set; }
        public long Damage { get; set; }
        public int Rating { get; set; }
        public DateTime Fetched { get; set; }

        public double WinRate => Battles > 0 ? 100.0 * Wins / Battles : 0;
        public int AvgDamage => Battles > 0 ? (int)(Damage / Battles) : 0;
    }

    readonly Config _cfg;
    readonly Dictionary<string, Entry> _cache = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _wanted = new(StringComparer.OrdinalIgnoreCase);
    bool _busy;
    static string CacheFile => Path.Combine(Config.Dir, "wot-cache.json");
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public string Status { get; private set; } = "";
    /// <summary>Pobrano nowe dane — przerysuj nakładkę (wątek UI).</summary>
    public event Action? Updated;

    public WotStats(Config cfg)
    {
        _cfg = cfg;
        try
        {
            if (File.Exists(CacheFile))
                foreach (var kv in JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(CacheFile)) ?? new())
                    _cache[kv.Key] = kv.Value;
        }
        catch { }
    }

    public bool Active => _cfg.WotStatsEnabled && _cfg.WotAppId.Trim() != "";

    string Host => _cfg.WotRegion switch { "na" => "api.worldoftanks.com", "asia" => "api.worldoftanks.asia", _ => "api.worldoftanks.eu" };

    /// <summary>Nick WoT dla osoby z TS (ręczne przypisanie albo nick z TS).</summary>
    public string NickFor(ClientInfo c) =>
        c.Uid != "" && _cfg.WotNicks.TryGetValue(c.Uid, out var n) && n.Trim() != "" ? n.Trim() : CleanNick(c.Nickname);

    /// <summary>Nicki WoT mogą mieć tylko litery, cyfry i _ (3–24 znaki) — reszta nicku z TS jest odrzucana.</summary>
    static string CleanNick(string s)
    {
        var t = new string(s.Where(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_').ToArray());
        return t.Length is >= 3 and <= 24 ? t : "";
    }

    /// <summary>Statystyki z pamięci podręcznej; brakujące/stare są dopisywane do kolejki pobierania.</summary>
    public Entry? Get(ClientInfo c)
    {
        if (!Active) return null;
        var nick = NickFor(c);
        if (nick == "") return null;
        if (_cache.TryGetValue(nick, out var e))
        {
            var ttl = e.NotFound ? TimeSpan.FromHours(24) : TimeSpan.FromHours(6);
            if (DateTime.Now - e.Fetched > ttl) _wanted.Add(nick);
            return e.NotFound ? null : e;
        }
        _wanted.Add(nick);
        return null;
    }

    /// <summary>Wywoływane co kilka sekund (wątek UI): pobiera zaległe nicki jednym zapytaniem (do 100).</summary>
    public async void Pump()
    {
        if (!Active || _busy || _wanted.Count == 0) return;
        _busy = true;
        var batch = _wanted.Take(100).ToList();
        foreach (var n in batch) _wanted.Remove(n);
        try
        {
            var found = await Fetch(batch);
            var now = DateTime.Now;
            foreach (var n in batch)
                _cache[n] = found.TryGetValue(n, out var e) ? e : new Entry { Nickname = n, NotFound = true, Fetched = now };
            Save();
            Status = L.T("Pobrano statystyki: {0}", found.Count);
            Updated?.Invoke();
        }
        catch (Exception ex)
        {
            Status = L.T("Błąd API Wargaming: {0}", ex.Message);
        }
        finally { _busy = false; }
    }

    /// <summary>Sprawdzenie jednego nicku (przycisk w ustawieniach).</summary>
    public async Task<Entry?> Lookup(string nick)
    {
        var r = await Fetch(new List<string> { nick.Trim() });
        if (r.TryGetValue(nick.Trim(), out var e)) { _cache[nick.Trim()] = e; Save(); return e; }
        return null;
    }

    async Task<Dictionary<string, Entry>> Fetch(List<string> nicks)
    {
        var app = Uri.EscapeDataString(_cfg.WotAppId.Trim());
        var result = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        if (nicks.Count == 0) return result;

        // 1) nick → account_id (wyszukiwanie dokładne, wiele nicków po przecinku)
        var list = await Get($"https://{Host}/wot/account/list/?application_id={app}&type=exact&search={Uri.EscapeDataString(string.Join(",", nicks))}");
        var ids = new Dictionary<long, string>();
        if (list is JsonArray arr)
            foreach (var a in arr)
            {
                long id = a?["account_id"]?.GetValue<long>() ?? 0;
                var nick = a?["nickname"]?.GetValue<string>() ?? "";
                if (id != 0) ids[id] = nick;
            }
        if (ids.Count == 0) return result;

        // 2) statystyki kont (do 100 na zapytanie)
        var info = await Get($"https://{Host}/wot/account/info/?application_id={app}&account_id={string.Join(",", ids.Keys)}" +
                             "&fields=nickname,global_rating,statistics.all.battles,statistics.all.wins,statistics.all.damage_dealt");
        var now = DateTime.Now;
        foreach (var (id, nick) in ids)
        {
            var d = info?[id.ToString()];
            if (d == null) continue;
            var all = d["statistics"]?["all"];
            var e = new Entry
            {
                AccountId = id,
                Nickname = d["nickname"]?.GetValue<string>() ?? nick,
                Battles = all?["battles"]?.GetValue<int>() ?? 0,
                Wins = all?["wins"]?.GetValue<int>() ?? 0,
                Damage = all?["damage_dealt"]?.GetValue<long>() ?? 0,
                Rating = d["global_rating"]?.GetValue<int>() ?? 0,
                Fetched = now,
            };
            // klucz = nick, o który pytaliśmy (wielkość liter może się różnić)
            var asked = nicks.FirstOrDefault(n => n.Equals(e.Nickname, StringComparison.OrdinalIgnoreCase)) ?? e.Nickname;
            result[asked] = e;
        }
        return result;
    }

    static async Task<JsonNode?> Get(string url)
    {
        var json = JsonNode.Parse(await Http.GetStringAsync(url));
        if (json?["status"]?.GetValue<string>() != "ok")
        {
            var err = json?["error"];
            throw new InvalidDataException($"{err?["message"]} ({err?["field"]})");
        }
        return json?["data"];
    }

    void Save()
    {
        try
        {
            Directory.CreateDirectory(Config.Dir);
            File.WriteAllText(CacheFile, JsonSerializer.Serialize(_cache));
        }
        catch { }
    }

    /// <summary>Kolor procentu zwycięstw jak w XVM: czerwony → pomarańczowy → żółty → zielony → niebieski → fioletowy.</summary>
    public static string WinRateColor(double wr) => wr switch
    {
        < 46 => "#FFFE0E00",
        < 48 => "#FFFE7903",
        < 50 => "#FFF8F400",
        < 52 => "#FF60FF00",
        < 54 => "#FF02C9B3",
        < 56 => "#FF3E8EFF",
        _ => "#FFD042F3",
    };

    public static string Short(int n) => n >= 10000 ? $"{n / 1000.0:0}k" : n >= 1000 ? $"{n / 1000.0:0.0}k" : n.ToString();
}
