using System.IO;
using System.Text.Json;

namespace TS6Overlay;

public sealed class DayPerson
{
    public string Nick { get; set; } = "";
    public double TalkSec { get; set; }
    /// <summary>Minuty obecności na serwerze (widoczny w TS).</summary>
    public double OnlineMin { get; set; }
    public int Joins { get; set; }
    public int Messages { get; set; }
}

public sealed class DayStats
{
    /// <summary>Twoje minuty na TeamSpeaku tego dnia.</summary>
    public double MyOnlineMin { get; set; }
    public Dictionary<string, DayPerson> People { get; set; } = new();
}

/// <summary>Zsumowane dane osoby za okres.</summary>
public sealed record PersonTotal(string Uid, string Nick, double TalkSec, double OnlineMin, int Joins, int Messages);

/// <summary>
/// Statystyki długoterminowe: pliki stats\RRRR-MM.json (dzień → osoby). Zapisywane co kilka minut i przy zamknięciu.
/// Wszystko na wątku UI.
/// </summary>
public sealed class LongTermStats
{
    static string Dir => Path.Combine(Config.Dir, "stats");
    readonly Dictionary<string, Dictionary<string, DayStats>> _months = new();
    readonly HashSet<string> _dirty = new();
    static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    Dictionary<string, DayStats> Month(string key)
    {
        if (_months.TryGetValue(key, out var m)) return m;
        m = new();
        try
        {
            var f = Path.Combine(Dir, key + ".json");
            if (File.Exists(f)) m = JsonSerializer.Deserialize<Dictionary<string, DayStats>>(File.ReadAllText(f)) ?? new();
        }
        catch { }
        _months[key] = m;
        return m;
    }

    DayStats Day(DateTime d)
    {
        var month = Month(d.ToString("yyyy-MM"));
        var key = d.ToString("yyyy-MM-dd");
        if (!month.TryGetValue(key, out var day)) month[key] = day = new DayStats();
        _dirty.Add(d.ToString("yyyy-MM"));
        return day;
    }

    DayPerson Person(DateTime d, string uid, string nick)
    {
        var day = Day(d);
        if (!day.People.TryGetValue(uid, out var p)) day.People[uid] = p = new DayPerson();
        if (nick != "") p.Nick = nick;
        return p;
    }

    public void AddTalk(string uid, string nick, double seconds)
    {
        if (uid == "" || seconds <= 0) return;
        Person(DateTime.Now, uid, nick).TalkSec += seconds;
    }

    public void AddJoin(string uid, string nick) { if (uid != "") Person(DateTime.Now, uid, nick).Joins++; }
    public void AddMessage(string uid, string nick) { if (uid != "") Person(DateTime.Now, uid, nick).Messages++; }

    /// <summary>Wywoływane co minutę: kto jest na serwerze i czy Ty jesteś połączony.</summary>
    public void AddOnlineMinute(IEnumerable<ClientInfo> people, bool meOnline)
    {
        var now = DateTime.Now;
        if (meOnline) Day(now).MyOnlineMin += 1;
        foreach (var c in people)
            if (c.Uid != "") Person(now, c.Uid, c.Nickname).OnlineMin += 1;
    }

    public void Save()
    {
        if (_dirty.Count == 0) return;
        try
        {
            Directory.CreateDirectory(Dir);
            foreach (var key in _dirty)
                File.WriteAllText(Path.Combine(Dir, key + ".json"), JsonSerializer.Serialize(_months[key], Json));
            _dirty.Clear();
        }
        catch { }
    }

    /// <summary>Dni z okresu (włącznie), najstarszy pierwszy.</summary>
    public List<(DateTime date, DayStats stats)> Days(DateTime from, DateTime to)
    {
        var list = new List<(DateTime, DayStats)>();
        for (var d = from.Date; d <= to.Date; d = d.AddDays(1))
        {
            var month = Month(d.ToString("yyyy-MM"));
            list.Add((d, month.TryGetValue(d.ToString("yyyy-MM-dd"), out var s) ? s : new DayStats()));
        }
        return list;
    }

    /// <summary>Najstarszy dzień z danymi (do zakresu „wszystko”).</summary>
    public DateTime FirstDay()
    {
        try
        {
            var first = Directory.Exists(Dir) ? Directory.GetFiles(Dir, "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(x => x).FirstOrDefault() : null;
            if (first != null && DateTime.TryParse(first + "-01", out var d))
            {
                var m = Month(first);
                var day = m.Keys.OrderBy(x => x).FirstOrDefault();
                return day != null && DateTime.TryParse(day, out var dd) ? dd : d;
            }
        }
        catch { }
        return DateTime.Today;
    }

    public List<PersonTotal> Totals(DateTime from, DateTime to) =>
        Days(from, to).SelectMany(d => d.stats.People)
            .GroupBy(kv => kv.Key)
            .Select(g => new PersonTotal(g.Key, g.Select(x => x.Value.Nick).LastOrDefault(n => n != "") ?? g.Key,
                g.Sum(x => x.Value.TalkSec), g.Sum(x => x.Value.OnlineMin), g.Sum(x => x.Value.Joins), g.Sum(x => x.Value.Messages)))
            .ToList();
}

/// <summary>Archiwum czatu: chat\RRRR-MM-DD.jsonl — jedna wiadomość na linię.</summary>
public static class ChatArchive
{
    public static string Dir => Path.Combine(Config.Dir, "chat");

    public sealed record Entry(DateTime Time, string Kind, string Server, string Text);

    public static void Append(Notice n, string server)
    {
        if (n.Kind is not (NoticeKind.Message or NoticeKind.Poke)) return;
        try
        {
            Directory.CreateDirectory(Dir);
            var e = new Entry(DateTime.Now, n.Kind.ToString(), server, n.Text);
            File.AppendAllText(Path.Combine(Dir, DateTime.Now.ToString("yyyy-MM-dd") + ".jsonl"), JsonSerializer.Serialize(e) + "\n");
        }
        catch { }
    }

    /// <summary>Wyszukiwanie (bez rozróżniania wielkości liter), najnowsze pierwsze.</summary>
    public static List<Entry> Search(string query, DateTime from, int max = 1000)
    {
        var result = new List<Entry>();
        if (!Directory.Exists(Dir)) return result;
        foreach (var f in Directory.GetFiles(Dir, "*.jsonl").OrderByDescending(x => x))
        {
            if (DateTime.TryParse(Path.GetFileNameWithoutExtension(f), out var day) && day < from.Date) break;
            string[] lines;
            try { lines = File.ReadAllLines(f); } catch { continue; }
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                Entry? e;
                try { e = JsonSerializer.Deserialize<Entry>(lines[i]); } catch { continue; }
                if (e == null || e.Time < from) continue;
                if (query != "" && !e.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                                && !e.Server.Contains(query, StringComparison.CurrentCultureIgnoreCase)) continue;
                result.Add(e);
                if (result.Count >= max) return result;
            }
        }
        return result;
    }
}
