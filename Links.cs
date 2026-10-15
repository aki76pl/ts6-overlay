using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows.Documents;
using System.Windows.Media;

namespace TS6Overlay;

/// <summary>
/// Linki w wiadomościach: wyszukiwanie, bezpieczne otwieranie (tylko http/https, w domyślnej przeglądarce)
/// i budowanie tekstu z klikalnymi linkami.
/// </summary>
public static class Links
{
    static readonly Regex Url = new(@"\bhttps?://[^\s<>""'`]+", RegexOptions.IgnoreCase);

    /// <summary>Adresy http/https z tekstu (bez kropki/przecinka/nawiasu na końcu zdania).</summary>
    public static List<Uri> Find(string text)
    {
        var list = new List<Uri>();
        foreach (Match m in Url.Matches(text))
            if (TryParse(Clean(m.Value), out var u)) list.Add(u);
        return list;
    }

    static string Clean(string s)
    {
        s = s.TrimEnd('.', ',', ';', ':', '!', '?', '»', '”');
        // nawias zamykający tylko, jeśli w adresie nie ma otwierającego (np. link w nawiasie)
        if (s.EndsWith(')') && !s.Contains('(')) s = s[..^1];
        return s;
    }

    static bool TryParse(string s, out Uri uri) =>
        Uri.TryCreate(s, UriKind.Absolute, out uri!) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && uri.Host != "";

    /// <summary>Otwiera adres w domyślnej przeglądarce — wyłącznie http/https.</summary>
    public static bool Open(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); return true; }
        catch { return false; }
    }

    /// <summary>Krótka, czytelna postać: domena + początek ścieżki (żeby było widać, dokąd prowadzi).</summary>
    public static string Display(Uri u)
    {
        var host = u.Host.StartsWith("www.") ? u.Host[4..] : u.Host;
        var rest = u.PathAndQuery == "/" ? "" : u.PathAndQuery;
        if (rest.Length > 28) rest = rest[..27] + "…";
        return host + rest;
    }

    /// <summary>Tekst dla lektora: adresy zastąpione słowem „link” z domeną.</summary>
    public static string Speakable(string text) =>
        Url.Replace(text, m =>
        {
            var c = Clean(m.Value);
            return TryParse(c, out var u) ? L.T("link do {0}", u.Host.StartsWith("www.") ? u.Host[4..] : u.Host) + m.Value[c.Length..] : m.Value;
        });

    /// <summary>Elementy tekstu z klikalnymi linkami (Hyperlink z adresem w NavigateUri).</summary>
    public static IEnumerable<Inline> Inlines(string text, Brush linkBrush, bool attachClick = true)
    {
        int pos = 0;
        foreach (Match m in Url.Matches(text))
        {
            var raw = Clean(m.Value);
            if (!TryParse(raw, out var u)) continue;
            if (m.Index > pos) yield return new Run(text[pos..m.Index]);
            var link = new Hyperlink(new Run("🔗 " + Display(u))) { NavigateUri = u, Foreground = linkBrush, ToolTip = u.AbsoluteUri };
            if (attachClick) link.Click += (_, _) => Open(u);
            yield return link;
            pos = m.Index + raw.Length;
        }
        if (pos < text.Length) yield return new Run(text[pos..]);
    }
}
