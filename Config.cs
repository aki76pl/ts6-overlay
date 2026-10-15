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

/// <summary>Profil nakładki dla gry: własna pozycja, rozmiar, przezroczystość i motyw.</summary>
public sealed class GameProfile
{
    /// <summary>Nazwa procesu bez .exe, np. cs2.</summary>
    public string Game { get; set; } = "";
    public double Left { get; set; } = 20;
    public double Top { get; set; } = 200;
    public double Scale { get; set; } = 1.0;
    public double Opacity { get; set; } = 0.9;
    /// <summary>Pusty = motyw główny.</summary>
    public string Theme { get; set; } = "";
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
    /// <summary>"pl", "en" albo "" = wg języka Windows.</summary>
    public string Language { get; set; } = "";
    /// <summary>Flagi krajów przy nickach.</summary>
    public bool ShowFlags { get; set; } = true;
    /// <summary>Ostrzeżenie, gdy ktoś na Twoim kanale nagrywa.</summary>
    public bool RecordingWarning { get; set; } = true;
    public bool SoundOnRecording { get; set; } = true;
    /// <summary>Sprawdzaj przy starcie, czy jest nowa wersja Aslain's WoT Modpack.</summary>
    public bool AslainNotify { get; set; } = false;
    /// <summary>Data ważności aktywacji usług XVM (z modxvm.com); null = nie ustawiono.</summary>
    public DateTime? XvmExpires { get; set; }
    /// <summary>Ile dni przed wygaśnięciem przypominać.</summary>
    public int XvmRemindDays { get; set; } = 3;
    public DateTime XvmLastReminder { get; set; } = DateTime.MinValue;
    /// <summary>Statystyki WoT osób z TS (API Wargaming).</summary>
    public bool WotStatsEnabled { get; set; } = false;
    public string WotAppId { get; set; } = "";
    /// <summary>"eu", "na" albo "asia".</summary>
    public string WotRegion { get; set; } = "eu";
    public bool WotShowInOverlay { get; set; } = true;
    /// <summary>Ręczne przypisanie: UID w TS → nick w WoT.</summary>
    public Dictionary<string, string> WotNicks { get; set; } = new();
    public string AslainLastSeen { get; set; } = "";
    /// <summary>Pokazuj wszystkie serwery, z którymi połączony jest TS (nie tylko aktywny).</summary>
    public bool ShowAllServers { get; set; } = true;

    // --- dźwięki ---
    /// <summary>Dźwięk, gdy ktoś wchodzi na Twój kanał.</summary>
    public bool SoundOnJoin { get; set; } = true;
    /// <summary>Dźwięk, gdy ktoś opuszcza Twój kanał.</summary>
    public bool SoundOnLeave { get; set; } = false;
    public bool SoundOnMessage { get; set; } = true;
    public bool SoundOnPoke { get; set; } = true;
    /// <summary>Własne dźwięki przypisane do zdarzeń: nazwa SoundKind → plik w %APPDATA%\TS6Overlay\sounds.</summary>
    public Dictionary<string, string> SoundFiles { get; set; } = new();
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
    /// <summary>Podpowiedź, gdy gra działa w wyłącznym pełnym ekranie (nakładka niewidoczna).</summary>
    public bool FullscreenHint { get; set; } = true;

    // --- lektor (czytanie na głos) ---
    public bool TtsEnabled { get; set; } = false;
    public bool TtsJoin { get; set; } = true;
    public bool TtsLeave { get; set; } = false;
    public bool TtsMessages { get; set; } = true;
    public bool TtsPokes { get; set; } = true;
    public bool TtsFriends { get; set; } = true;
    /// <summary>Czytaj tylko, gdy na pierwszym planie jest gra.</summary>
    public bool TtsOnlyInGame { get; set; } = false;
    public string TtsVoice { get; set; } = "";
    /// <summary>Tempo -10…10.</summary>
    public int TtsRate { get; set; } = 1;
    public int TtsVolume { get; set; } = 80;

    // --- podgląd pod klawiszem ---
    public SoundBind PeekServerBind { get; set; } = new() { Id = "peek-server", Name = "Podgląd serwera" };
    public SoundBind PeekMessagesBind { get; set; } = new() { Id = "peek-messages", Name = "Podgląd wiadomości" };
    /// <summary>Przeglądarka do linków z czatu; pusta = domyślna przeglądarka Windows.</summary>
    public string LinkBrowser { get; set; } = "";
    /// <summary>Otwiera ostatni link z czatu (domyślnie Alt+K).</summary>
    public SoundBind OpenLinkBind { get; set; } = new() { Id = "open-link", Name = "Otwórz ostatni link", Modifiers = 1, Key = 0x4B };
    /// <summary>Jak długo wiadomość z linkiem jest widoczna na nakładce (sekundy).</summary>
    public int LinkSeconds { get; set; } = 5;

    // --- profile gier ---
    public List<GameProfile> Profiles { get; set; } = new();

    // --- obserwowane kanały i znajomi ---
    public List<string> WatchedChannels { get; set; } = new();
    public bool WatchedHideEmpty { get; set; } = false;
    public bool WatchedNotify { get; set; } = false;
    /// <summary>Powiadomienie, gdy ulubiony wchodzi na serwer.</summary>
    public bool FriendOnline { get; set; } = true;
    public bool FriendOffline { get; set; } = false;

    // --- osoby ---
    public List<Favorite> Favorites { get; set; } = new();
    public List<Ignored> IgnoredClients { get; set; } = new();

    // --- bindy (soundboard) ---
    public List<SoundBind> Binds { get; set; } = new();
    /// <summary>Skrót „zatrzymaj wszystkie dźwięki” (bindy i powiadomienia). Używane są tylko Key i Modifiers.</summary>
    public SoundBind StopBind { get; set; } = new() { Id = "stop", Name = "Zatrzymaj wszystkie dźwięki" };
    /// <summary>Nazwa urządzenia wyjściowego dla bindów; pusta = domyślne (tylko Ty).</summary>
    public string BindDeviceName { get; set; } = "";
    /// <summary>Gdy wybrano inne urządzenie — odtwarzaj też na domyślnym, żebyś słyszał.</summary>
    public bool BindAlsoLocal { get; set; } = true;

    // --- panel na telefonie ---
    public bool PhoneEnabled { get; set; } = false;
    public int PhonePort { get; set; } = 5897;
    /// <summary>Losowy klucz w adresie panelu — bez niego nikt w sieci nie odpali bindów.</summary>
    public string PhoneKey { get; set; } = "";

    // --- Discord ---
    public bool DiscordEnabled { get; set; } = false;
    /// <summary>Application ID z discord.com/developers/applications.</summary>
    public string DiscordAppId { get; set; } = "";
    public bool DiscordShowChannel { get; set; } = true;
    public bool DiscordShowServer { get; set; } = false;
    /// <summary>Nazwa obrazka (Rich Presence Assets) albo adres https; pusty = bez obrazka.</summary>
    public string DiscordLargeImage { get; set; } = "";

    // --- podświetlenie RGB (OpenRGB) ---
    public bool RgbEnabled { get; set; } = false;
    public string RgbHost { get; set; } = "127.0.0.1";
    public int RgbPort { get; set; } = 6742;
    /// <summary>Kolor błysku dla zdarzenia (NoticeKind albo "MuteWarning"); pusty = bez błysku.</summary>
    public Dictionary<string, string> RgbColors { get; set; } = new()
    {
        ["Poke"] = "#FFFF8C00",
        ["Friend"] = "#FF00FF66",
        ["Join"] = "",
        ["Message"] = "",
        ["MuteWarning"] = "",
        ["Recording"] = "#FFFF0000",
    };
    public int RgbFlashes { get; set; } = 3;
    public int RgbFlashMs { get; set; } = 180;

    public string RgbColorFor(string kind) => RgbEnabled && RgbColors.TryGetValue(kind, out var c) ? c : "";

    // --- historia ---
    public bool KeepChatArchive { get; set; } = true;
    public bool KeepLongTermStats { get; set; } = true;

    // --- OBS ---
    public bool ObsEnabled { get; set; } = false;
    public int ObsPort { get; set; } = 5898;

    // --- aktualizacje ---
    public bool AutoUpdateCheck { get; set; } = true;
    public string SkippedVersion { get; set; } = "";
    /// <summary>Kiedy ostatnio pokazano dymek o aktualizacji — kolejny najwcześniej po 7 dniach.</summary>
    public DateTime UpdateNotifiedAt { get; set; } = DateTime.MinValue;
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
