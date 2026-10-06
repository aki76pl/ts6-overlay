using System.IO;
using System.Text.Json;

namespace TS6Overlay;

public sealed class Favorite
{
    public string Uid { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string Color { get; set; } = "#FFFFD700";
    /// <summary>Własny dźwięk wejścia (wav/mp3); pusty = wbudowany sygnał ulubionego.</summary>
    public string SoundPath { get; set; } = "";
}

public sealed class Ignored
{
    public string Uid { get; set; } = "";
    public string Nickname { get; set; } = "";
}

public sealed class Config
{
    public string ApiKey { get; set; } = "";
    public double Left { get; set; } = 20;
    public double Top { get; set; } = 200;
    public double Opacity { get; set; } = 0.9;
    public double Scale { get; set; } = 1.0;
    /// <summary>Pokazuj pełną listę osób na kanale (false = tylko mówiący + powiadomienia).</summary>
    public bool ShowChannelList { get; set; } = true;
    /// <summary>Ile sekund wisi powiadomienie „wszedł / wyszedł”.</summary>
    public int EventSeconds { get; set; } = 6;
    public string Theme { get; set; } = "Terminal";

    // --- dźwięki ---
    /// <summary>Dźwięk, gdy ktoś wchodzi na Twój kanał.</summary>
    public bool SoundOnJoin { get; set; } = true;
    /// <summary>Dźwięk, gdy ktoś opuszcza Twój kanał.</summary>
    public bool SoundOnLeave { get; set; } = false;
    public bool SoundOnMessage { get; set; } = true;
    public bool SoundOnPoke { get; set; } = true;
    /// <summary>Głośność sygnałów 0–100.</summary>
    public int Volume { get; set; } = 60;

    // --- wiadomości ---
    public bool ShowPrivateMessages { get; set; } = true;
    public bool ShowChannelMessages { get; set; } = true;
    public bool ShowServerMessages { get; set; } = false;
    public bool ShowPokes { get; set; } = true;
    public int MessageSeconds { get; set; } = 10;

    // --- ostrzeżenie o wyciszonym mikrofonie ---
    public bool MuteWarning { get; set; } = true;
    /// <summary>Wykrywaj mowę do wyciszonego mikrofonu (nasłuch tylko, gdy mikrofon jest wyciszony).</summary>
    public bool MuteVoiceDetect { get; set; } = true;
    /// <summary>Próg głośności mowy 1–100 (niżej = czulej).</summary>
    public int MuteSensitivity { get; set; } = 12;
    public bool MuteWarningSound { get; set; } = true;

    // --- autoukrywanie ---
    public bool AutoHide { get; set; } = false;
    public int AutoHideSeconds { get; set; } = 5;
    /// <summary>"Fade" = nakładka blednie, "Header" = zostaje sam nagłówek.</summary>
    public string AutoHideMode { get; set; } = "Fade";
    public double AutoHideOpacity { get; set; } = 0.15;

    // --- tylko w grach ---
    public bool GameOnly { get; set; } = false;
    public List<string> Games { get; set; } = new();
    /// <summary>Traktuj każdą aplikację na pełnym ekranie (poza przeglądarkami) jak grę.</summary>
    public bool GameFullscreenAny { get; set; } = true;

    // --- osoby ---
    public List<Favorite> Favorites { get; set; } = new();
    public List<Ignored> IgnoredClients { get; set; } = new();

    // --- OBS ---
    public bool ObsEnabled { get; set; } = false;
    public int ObsPort { get; set; } = 5898;

    // --- aktualizacje ---
    public bool AutoUpdateCheck { get; set; } = true;
    public string SkippedVersion { get; set; } = "";
    public bool InstallPromptShown { get; set; } = false;

    public bool LogRawEvents { get; set; } = false;

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TS6Overlay");
    static string FilePath => Path.Combine(Dir, "config.json");
    public static string LogPath => Path.Combine(Dir, "events.log");
    public static string ThemesDir => Path.Combine(Dir, "themes");

    public Favorite? FavoriteFor(string uid) => uid == "" ? null : Favorites.FirstOrDefault(f => f.Uid == uid);
    public bool IsIgnored(string uid) => uid != "" && IgnoredClients.Any(i => i.Uid == uid);

    public static Config Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath)) ?? new Config();
        }
        catch { }
        return new Config();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
