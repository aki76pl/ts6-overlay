using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace TS6Overlay;

/// <summary>
/// Kopia wszystkich ustawień w jednym pliku .ts6backup (zip): config.json + motywy + dźwięki.
/// Ścieżki dźwięków zapisujemy względnie, bo na innym komputerze profil użytkownika może mieć inną nazwę.
/// Klucz API TeamSpeaka nie trafia do kopii — każdy komputer dostaje własną zgodę w TS.
/// </summary>
public static class Backup
{
    public const string Extension = ".ts6backup";
    const string Marker = "%TS6DIR%";

    public static void Export(Config cfg, string path)
    {
        cfg.Save();
        var json = File.ReadAllText(Path.Combine(Config.Dir, "config.json"));
        var copy = JsonSerializer.Deserialize<Config>(json)!;
        copy.ApiKey = "";
        RewritePaths(copy, p => p.StartsWith(Config.Dir, StringComparison.OrdinalIgnoreCase) ? Marker + p[Config.Dir.Length..] : p);

        if (File.Exists(path)) File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("config.json");
        using (var w = new StreamWriter(entry.Open()))
            w.Write(JsonSerializer.Serialize(copy, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var dir in new[] { "themes", "sounds" })
        {
            var full = Path.Combine(Config.Dir, dir);
            if (!Directory.Exists(full)) continue;
            foreach (var f in Directory.GetFiles(full))
                zip.CreateEntryFromFile(f, dir + "/" + Path.GetFileName(f));
        }
    }

    /// <summary>Wczytuje kopię: nadpisuje ustawienia, motywy i dźwięki. Zachowuje klucz API i pozycję nakładki z tego komputera.</summary>
    public static void Import(Config current, string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var cfgEntry = zip.GetEntry("config.json") ?? throw new InvalidDataException(L.T("To nie jest kopia ustawień TS6 Overlay."));
        Config imported;
        using (var r = new StreamReader(cfgEntry.Open()))
            imported = JsonSerializer.Deserialize<Config>(r.ReadToEnd()) ?? throw new InvalidDataException(L.T("Uszkodzony plik ustawień."));

        foreach (var e in zip.Entries)
        {
            var parts = e.FullName.Split('/');
            if (parts.Length != 2 || parts[1] == "" || (parts[0] != "themes" && parts[0] != "sounds")) continue;
            var name = Path.GetFileName(parts[1]);   // bez wychodzenia poza folder
            var dest = Path.Combine(Config.Dir, parts[0], name);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            e.ExtractToFile(dest, true);
        }

        RewritePaths(imported, p => p.StartsWith(Marker) ? Config.Dir + p[Marker.Length..] : p);
        imported.ApiKey = current.ApiKey;
        imported.InstallPromptShown = true;
        imported.Save();
    }

    static void RewritePaths(Config c, Func<string, string> f)
    {
        foreach (var k in c.SoundFiles.Keys.ToList()) c.SoundFiles[k] = f(c.SoundFiles[k]);
        foreach (var b in c.Binds) b.SoundPath = f(b.SoundPath);
        foreach (var fav in c.Favorites) fav.SoundPath = f(fav.SoundPath);
    }
}
