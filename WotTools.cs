using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace TS6Overlay;

/// <summary>
/// Narzędzia World of Tanks: sprawdzanie i pobieranie Aslain's WoT Modpack, odnośniki XVM.
/// Program tylko pobiera instalator na żądanie — instalację uruchamia i przeprowadza użytkownik.
/// </summary>
public static class WotTools
{
    /// <summary>Polski wątek z pobieraniem (zawiera numer wersji i link do instalatora).</summary>
    public const string AslainPagePl = "https://aslain.com/index.php?/topic/2-pobierz-%E2%98%85-world-of-tanks-%E2%98%85-modpack/";
    public const string AslainPageEn = "https://aslain.com/index.php?/topic/13-download-%E2%98%85-world-of-tanks-%E2%98%85-modpack/";
    /// <summary>Strona modpacka w oficjalnym WG Mod Hub.</summary>
    public const string WgModHub = "https://wgmods.net/46/";
    /// <summary>Nowa aplikacja Aslaina (stały adres, zawsze najnowsza).</summary>
    public const string ModManagerUrl = "https://repo.aslain.com/wot_modmanager/Aslain_WoT_Mod_Manager_Setup.exe";
    public const string XvmHome = "https://modxvm.com/en/";
    public const string XvmServicesGuide = "https://modxvm.com/en/network-services-xvm/";
    public const string FairPlay = "https://worldoftanks.eu/en/news/general-news/ban-wave-jan-2026/";

    public sealed record AslainRelease(string Version, string? InstallerUrl);

    static HttpClient Http()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) TS6Overlay/" + Updater.Current);
        return h;
    }

    /// <summary>Aktualna wersja modpacka i link do klasycznego instalatora (ze strony Aslaina).</summary>
    public static async Task<AslainRelease> CheckAslainAsync()
    {
        using var http = Http();
        var html = await http.GetStringAsync(AslainPagePl);
        // Strona zawiera też historię zmian ze starszymi numerami — bierzemy najwyższą wersję i numer wydania.
        var best = Regex.Matches(html, @"v\.?\s?(?<v>\d+(?:\.\d+){2,3})\s*#(?<n>\d+)(?:\s*\((?<d>\d{2}-\d{2}-\d{4})\))?")
            .Select(m => (m, ver: Version.Parse(m.Groups["v"].Value), n: int.Parse(m.Groups["n"].Value)))
            .OrderByDescending(x => x.ver).ThenByDescending(x => x.n)
            .FirstOrDefault();
        if (best.m == null) throw new InvalidDataException(L.T("Nie udało się odczytać wersji ze strony Aslaina."));
        string text = $"v{best.m.Groups["v"].Value} #{best.m.Groups["n"].Value}" + (best.m.Groups["d"].Success ? $" ({best.m.Groups["d"].Value})" : "");
        // Link do instalatora: ten z najwyższą wersją w nazwie pliku.
        var exe = Regex.Matches(html, @"https://filez\.aslain\.com/[^""'<>\s]+\.exe", RegexOptions.IgnoreCase)
            .Select(m => m.Value).Distinct()
            .OrderByDescending(u => Regex.Match(u, @"v\.?(\d+(?:\.\d+){2,3})_(\d+)") is { Success: true } mm
                ? (Version.Parse(mm.Groups[1].Value), int.Parse(mm.Groups[2].Value)) : (new Version(0, 0), 0))
            .FirstOrDefault();
        return new AslainRelease(text, exe);
    }

    /// <summary>Pobiera instalator do folderu Pobrane; zwraca ścieżkę pliku.</summary>
    public static async Task<string> DownloadAsync(string url, IProgress<double>? progress, CancellationToken ct = default)
    {
        var name = Path.GetFileName(new Uri(url).LocalPath);
        var dest = Path.Combine(DownloadsFolder(), name);
        var tmp = dest + ".part";
        using (var http = Http())
        using (var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? 0;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(tmp);
            var buf = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (total > 0) progress?.Report((double)done / total);
            }
        }
        if (new FileInfo(tmp).Length < 500_000) { File.Delete(tmp); throw new InvalidDataException(L.T("Pobrany plik jest uszkodzony.")); }
        if (File.Exists(dest)) File.Delete(dest);
        File.Move(tmp, dest);
        return dest;
    }

    public static void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    /// <summary>Pokazuje plik w Eksploratorze (zaznaczony).</summary>
    public static void Reveal(string file) => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\""));

    static string DownloadsFolder()
    {
        try
        {
            if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out var p) == 0)
            {
                var path = Marshal.PtrToStringUni(p);
                Marshal.FreeCoTaskMem(p);
                if (!string.IsNullOrEmpty(path)) return path;
            }
        }
        catch { }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);
}
