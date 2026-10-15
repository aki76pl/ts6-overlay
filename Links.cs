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

    /// <summary>Ścieżka przeglądarki wybranej w ustawieniach; pusta = domyślna przeglądarka Windows.</summary>
    public static string BrowserPath { get; set; } = "";

    /// <summary>Wywoływane przy otwieraniu linku (nakładka pokazuje „Otwieram: …”).</summary>
    public static event Action<Uri>? Opening;

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);

    /// <summary>
    /// Otwiera adres (tylko http/https) w przeglądarce — w tle, żeby nie blokować nakładki.
    /// AllowSetForegroundWindow pozwala przeglądarce wyjść na pierwszy plan nad grą
    /// (bez tego Windows zostawia jej okno w tle, bo nakładka sama nigdy nie jest aktywna).
    /// </summary>
    public static bool Open(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        AllowSetForegroundWindow(-1);   // ASFW_ANY
        Opening?.Invoke(uri);
        var url = uri.AbsoluteUri;
        var browser = BrowserPath;
        Task.Run(() =>
        {
            try
            {
                if (browser != "" && System.IO.File.Exists(browser))
                    Process.Start(new ProcessStartInfo(browser) { ArgumentList = { url }, UseShellExecute = false });
                else
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
            }
        });
        return true;
    }

    /// <summary>Zainstalowane przeglądarki (nazwa, ścieżka) z rejestru App Paths.</summary>
    public static List<(string name, string path)> InstalledBrowsers()
    {
        var list = new List<(string, string)>();
        foreach (var (name, exe) in new[] { ("Google Chrome", "chrome.exe"), ("Microsoft Edge", "msedge.exe"), ("Mozilla Firefox", "firefox.exe"),
                                             ("Opera", "opera.exe"), ("Brave", "brave.exe"), ("Vivaldi", "vivaldi.exe") })
        {
            foreach (var root in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
            {
                try
                {
                    using var k = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exe);
                    if (k?.GetValue(null) is string p && System.IO.File.Exists(p.Trim('"'))) { list.Add((name, p.Trim('"'))); break; }
                }
                catch { }
            }
        }
        return list;
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
