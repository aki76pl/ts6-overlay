using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json.Nodes;

namespace TS6Overlay;

/// <summary>Aktualizacje z GitHub Releases: ostatnie wydanie z plikiem TS6Overlay.exe.</summary>
public static class Updater
{
    public const string Repo = "aki76pl/ts6-overlay";
    public const string AssetName = "TS6Overlay.exe";
    public static string ReleasesPage => $"https://github.com/{Repo}/releases";

    public static Version Current =>
        Assembly.GetExecutingAssembly().GetName().Version is { } v ? new Version(v.Major, v.Minor, v.Build) : new Version(0, 0, 0);

    public sealed record Release(Version Version, string Tag, string Notes, string DownloadUrl);

    static HttpClient Http()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("TS6Overlay/" + Current);
        return h;
    }

    /// <summary>Najnowsze wydanie, jeśli jest nowsze od bieżącej wersji; inaczej null.</summary>
    public static async Task<Release?> CheckAsync()
    {
        using var http = Http();
        var json = JsonNode.Parse(await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest"));
        string tag = json?["tag_name"]?.GetValue<string>() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var v)) return null;
        string? url = null;
        if (json?["assets"] is JsonArray assets)
            foreach (var a in assets)
                if (string.Equals(a?["name"]?.GetValue<string>(), AssetName, StringComparison.OrdinalIgnoreCase))
                    url = a?["browser_download_url"]?.GetValue<string>();
        if (url == null || v <= Current) return null;
        return new Release(v, tag, json?["body"]?.GetValue<string>() ?? "", url);
    }

    /// <summary>
    /// Pobiera nowy plik, podmienia uruchomiony exe (Windows pozwala zmienić nazwę działającego pliku)
    /// i uruchamia nową wersję. Wywołujący powinien zaraz potem zamknąć aplikację.
    /// </summary>
    public static async Task InstallAsync(Release r, IProgress<int>? progress = null)
    {
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Brak ścieżki programu.");
        string dir = Path.GetDirectoryName(exe)!;
        string tmp = Path.Combine(dir, "TS6Overlay.new");
        string old = Path.Combine(dir, "TS6Overlay.old");

        using (var http = Http())
        using (var resp = await http.GetAsync(r.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? 0;
            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var dst = File.Create(tmp);
            var buf = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n));
                done += n;
                if (total > 0) progress?.Report((int)(done * 100 / total));
            }
        }
        if (new FileInfo(tmp).Length < 100_000) throw new InvalidDataException(L.T("Pobrany plik jest uszkodzony."));

        if (File.Exists(old)) File.Delete(old);
        File.Move(exe, old);
        File.Move(tmp, exe);
        Process.Start(new ProcessStartInfo(exe, "--updated") { UseShellExecute = true });
    }

    /// <summary>Sprząta po poprzedniej aktualizacji.</summary>
    public static void Cleanup()
    {
        try
        {
            string dir = Path.GetDirectoryName(Environment.ProcessPath ?? "") ?? "";
            foreach (var f in new[] { "TS6Overlay.old", "TS6Overlay.new" })
            {
                var p = Path.Combine(dir, f);
                if (File.Exists(p)) File.Delete(p);
            }
        }
        catch { }
    }
}
