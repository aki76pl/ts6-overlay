using System.IO;
using System.Text;

namespace TS6Overlay;

public sealed class PersonStats
{
    public string Key = "";
    public string Nickname = "";
    public TimeSpan TalkTime;
    public DateTime? TalkingSince;
    public int Joins;
    public int Messages;

    public TimeSpan TotalTalk => TalkTime + (TalkingSince is { } s ? DateTime.Now - s : TimeSpan.Zero);
}

/// <summary>Statystyki bieżącej sesji: kto ile mówił, kto wchodził/wychodził. Tylko w pamięci, wątek UI.</summary>
public sealed class Stats
{
    public readonly Dictionary<string, PersonStats> People = new();
    public readonly List<(DateTime Time, string Text, NoticeKind Kind)> History = new();
    public DateTime Started { get; private set; } = DateTime.Now;
    public event Action? Changed;

    static string KeyOf(ClientInfo c) => c.Uid != "" ? c.Uid : "#" + c.Id;

    PersonStats Get(ClientInfo c)
    {
        var k = KeyOf(c);
        if (!People.TryGetValue(k, out var p)) People[k] = p = new PersonStats { Key = k };
        if (c.Nickname != "") p.Nickname = c.Nickname;
        return p;
    }

    public void OnTalk(ClientInfo c, bool talking)
    {
        var p = Get(c);
        if (talking) p.TalkingSince ??= DateTime.Now;
        else if (p.TalkingSince is { } s)
        {
            p.TalkTime += DateTime.Now - s;
            p.TalkingSince = null;
        }
        Changed?.Invoke();
    }

    public void OnNotice(Notice n)
    {
        History.Add((DateTime.Now, n.Text, n.Kind));
        if (History.Count > 500) History.RemoveAt(0);
        if (n.Who != null)
        {
            var p = Get(n.Who);
            if (n.Kind == NoticeKind.Join) p.Joins++;
            if (n.Kind == NoticeKind.Message) p.Messages++;
        }
        Changed?.Invoke();
    }

    public void Clear()
    {
        People.Clear();
        History.Clear();
        Started = DateTime.Now;
        Changed?.Invoke();
    }

    public void ExportCsv(string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Nick;Czas mówienia (s);Wejścia na kanał;Wiadomości");
        foreach (var p in People.Values.OrderByDescending(p => p.TotalTalk))
            sb.AppendLine($"{Csv(p.Nickname)};{(int)p.TotalTalk.TotalSeconds};{p.Joins};{p.Messages}");
        sb.AppendLine();
        sb.AppendLine("Czas;Zdarzenie");
        foreach (var h in History)
            sb.AppendLine($"{h.Time:yyyy-MM-dd HH:mm:ss};{Csv(h.Text)}");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    static string Csv(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
}
