using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;

namespace TS6Overlay;

/// <summary>
/// Flagi krajów przy nickach. Windows nie wyświetla emoji flag, więc używamy małych obrazków z flagcdn.com,
/// pobieranych raz i zapisywanych w %APPDATA%\TS6Overlay\flags. Bez internetu pokazujemy kod kraju (np. „PL”).
/// </summary>
public static class Flags
{
    static readonly Dictionary<string, BitmapImage?> Cache = new(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> Loading = new(StringComparer.OrdinalIgnoreCase);
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    static string Dir => Path.Combine(Config.Dir, "flags");

    /// <summary>Pobrano nową flagę — nakładkę trzeba przerysować (wątek UI).</summary>
    public static event Action? Loaded;

    /// <summary>Adres obrazka flagi (dla stron OBS i telefonu).</summary>
    public static string Url(string code) => $"https://flagcdn.com/20x15/{code.ToLowerInvariant()}.png";

    static bool Valid(string code) => code.Length == 2 && char.IsAsciiLetter(code[0]) && char.IsAsciiLetter(code[1]);

    /// <summary>Obrazek flagi albo null (wtedy pokaż kod tekstem). Wywoływać na wątku UI.</summary>
    public static BitmapImage? Get(string code)
    {
        if (!Valid(code)) return null;
        if (Cache.TryGetValue(code, out var img)) return img;
        var file = Path.Combine(Dir, code.ToLowerInvariant() + ".png");
        if (File.Exists(file))
        {
            img = LoadFile(file);
            Cache[code] = img;
            return img;
        }
        if (Loading.Add(code)) _ = Download(code, file);
        return null;
    }

    static BitmapImage? LoadFile(string file)
    {
        try
        {
            var b = new BitmapImage();
            b.BeginInit();
            b.CacheOption = BitmapCacheOption.OnLoad;
            b.UriSource = new Uri(file);
            b.EndInit();
            b.Freeze();
            return b;
        }
        catch { return null; }
    }

    static async Task Download(string code, string file)
    {
        try
        {
            var bytes = await Http.GetByteArrayAsync(Url(code));
            Directory.CreateDirectory(Dir);
            await File.WriteAllBytesAsync(file, bytes);
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                Cache[code] = LoadFile(file);
                Loaded?.Invoke();
            });
        }
        catch
        {
            // bez internetu: zostaje kod tekstowy; spróbujemy ponownie przy następnym uruchomieniu
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => Cache[code] = null);
        }
    }
}
