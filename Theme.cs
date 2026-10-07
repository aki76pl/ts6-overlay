using System.IO;
using System.Text.Json;
using System.Windows.Media;

namespace TS6Overlay;

/// <summary>Wygląd nakładki. Kolory jako #AARRGGBB.</summary>
public sealed record Theme
{
    public required string Name { get; init; }
    public string Panel { get; init; } = "#99101418";
    public string PanelBorder { get; init; } = "#00000000";
    public double BorderWidth { get; init; } = 0;
    public double Radius { get; init; } = 8;
    public string Font { get; init; } = "Segoe UI";
    public string Text { get; init; } = "#CCFFFFFF";
    public string TextTalking { get; init; } = "#FFFFFFFF";
    public string Header { get; init; } = "#CCFFFFFF";
    public string Talk { get; init; } = "#FF3DDC84";
    public string Whisper { get; init; } = "#FFF5A623";
    public string Idle { get; init; } = "#90FFFFFF";
    /// <summary>Tło wiersza osoby, która mówi (przezroczyste = brak).</summary>
    public string TalkRow { get; init; } = "#00000000";
    public string Join { get; init; } = "#E01E7A46";
    public string Leave { get; init; } = "#E08A2B2B";
    public string Info { get; init; } = "#E02B4A8A";
    public string NoticeText { get; init; } = "#FFFFFFFF";
    public string Message { get; init; } = "#E0465A78";
    public string Poke { get; init; } = "#E0B8860B";
    /// <summary>Pasek ostrzeżenia „mikrofon wyciszony”.</summary>
    public string Banner { get; init; } = "#E0C62828";
    public string Muted { get; init; } = "#FFCD5C5C";
    public string Accent { get; init; } = "#FFFFD700";
    /// <summary>Kwadratowe znaczniki zamiast kropek.</summary>
    public bool SquareDots { get; init; }
    /// <summary>Cień pod tekstem: czytelność bez tła panelu.</summary>
    public bool TextShadow { get; init; }
    public bool Glow { get; init; } = true;

    public static Brush B(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    public static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static readonly Theme[] Builtin =
    {
        new() { Name = "Ciemny" },

        new()
        {
            Name = "Jasny",
            Panel = "#E6F4F5F7", PanelBorder = "#33000000", BorderWidth = 1,
            Text = "#CC1C1F26", TextTalking = "#FF000000", Header = "#AA1C1F26",
            Talk = "#FF14A44D", Whisper = "#FFE08600", Idle = "#55000000", TalkRow = "#2214A44D",
            Join = "#F014A44D", Leave = "#F0C53030", Info = "#F02F6FD0", Muted = "#FFC53030",
            Accent = "#FF2F6FD0", Glow = false,
        },

        new()
        {
            Name = "Discord",
            Panel = "#F0313338", Radius = 6,
            Text = "#FFB5BAC1", TextTalking = "#FFFFFFFF", Header = "#FF949BA4",
            Talk = "#FF23A55A", Whisper = "#FFF0B232", Idle = "#FF80848E", TalkRow = "#334E5058",
            Join = "#F023A55A", Leave = "#F0DA373C", Info = "#F05865F2", Muted = "#FFDA373C",
            Accent = "#FF5865F2",
        },

        new()
        {
            Name = "Neon",
            Panel = "#CC07060F", PanelBorder = "#FF00E5FF", BorderWidth = 1, Radius = 2, Font = "Bahnschrift",
            Text = "#CCB8F7FF", TextTalking = "#FF00E5FF", Header = "#FFFF2BD6",
            Talk = "#FF00E5FF", Whisper = "#FFFF2BD6", Idle = "#4400E5FF", TalkRow = "#2200E5FF",
            Join = "#D000A3B8", Leave = "#D0B0127F", Info = "#D03A1C8C", Muted = "#FFFF2B6B",
            Accent = "#FFFF2BD6",
        },

        new()
        {
            Name = "Terminal",
            Panel = "#E0000000", PanelBorder = "#FF1F8F3A", BorderWidth = 1, Radius = 0, Font = "Consolas",
            Text = "#FF2FBF55", TextTalking = "#FF7CFF9E", Header = "#FF1F8F3A",
            Talk = "#FF7CFF9E", Whisper = "#FFE6D35A", Idle = "#FF145A26", TalkRow = "#221F8F3A",
            Join = "#E0103D1A", Leave = "#E04A1212", Info = "#E0102A3D", NoticeText = "#FF7CFF9E", Muted = "#FFFF5555",
            Message = "#E0102F35", Poke = "#E0403A08", Banner = "#E0701010",
            Accent = "#FF7CFF9E", SquareDots = true, Glow = false,
        },

        new()
        {
            Name = "Taktyczny",
            Panel = "#CC1E2116", PanelBorder = "#FF6B7A3A", BorderWidth = 1, Radius = 0, Font = "Bahnschrift",
            Text = "#FFC9C7A5", TextTalking = "#FFF2EFC9", Header = "#FF8E9B5A",
            Talk = "#FFB7D14A", Whisper = "#FFE0A030", Idle = "#665E6440", TalkRow = "#334A5520",
            Join = "#E0485A1E", Leave = "#E06E2A1E", Info = "#E03A4450", Muted = "#FFD0583A",
            Accent = "#FFB7D14A", SquareDots = true, Glow = false,
        },

        new()
        {
            Name = "Szkło",
            Panel = "#33FFFFFF", PanelBorder = "#55FFFFFF", BorderWidth = 1, Radius = 14,
            Text = "#E6FFFFFF", TextTalking = "#FFFFFFFF", Header = "#B3FFFFFF",
            Talk = "#FF6CF0B0", Whisper = "#FFFFD27A", Idle = "#66FFFFFF", TalkRow = "#226CF0B0",
            Join = "#9928B070", Leave = "#99C04848", Info = "#994870C0",
            TextShadow = true,
        },

        new()
        {
            Name = "Minimalny (bez tła)",
            Panel = "#00000000", Radius = 0,
            Text = "#E6FFFFFF", TextTalking = "#FFFFFFFF", Header = "#B3FFFFFF",
            Idle = "#66FFFFFF",
            Join = "#B01E7A46", Leave = "#B08A2B2B", Info = "#B02B4A8A",
            TextShadow = true,
        },
    };

    // ---------- motywy użytkownika (pliki *.ts6theme w %APPDATA%\TS6Overlay\themes) ----------

    public const string Extension = ".ts6theme";
    static List<Theme> _custom = new();
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Wbudowane + własne.</summary>
    public static IReadOnlyList<Theme> All => Builtin.Concat(_custom).ToList();

    public bool IsBuiltin => Builtin.Any(b => b.Name == Name);

    public static Theme ByName(string? name) =>
        All.FirstOrDefault(t => t.Name == name) ?? Builtin.First(t => t.Name == "Terminal");

    public static void LoadCustom()
    {
        var list = new List<Theme>();
        try
        {
            Directory.CreateDirectory(Config.ThemesDir);
            foreach (var f in Directory.GetFiles(Config.ThemesDir, "*" + Extension))
            {
                try
                {
                    var t = JsonSerializer.Deserialize<Theme>(File.ReadAllText(f));
                    if (t != null && t.Name != "" && !Builtin.Any(b => b.Name == t.Name) && t.IsValid()) list.Add(t);
                }
                catch { }
            }
        }
        catch { }
        _custom = list.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    static string FileFor(string name)
    {
        var safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(Config.ThemesDir, safe + Extension);
    }

    public void SaveCustom()
    {
        if (IsBuiltin) throw new InvalidOperationException(L.T("Nie można nadpisać wbudowanego motywu — nadaj inną nazwę."));
        Directory.CreateDirectory(Config.ThemesDir);
        File.WriteAllText(FileFor(Name), JsonSerializer.Serialize(this, Json));
        LoadCustom();
    }

    public static void DeleteCustom(string name)
    {
        try { File.Delete(FileFor(name)); } catch { }
        LoadCustom();
    }

    public void Export(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, Json));

    /// <summary>Wczytuje motyw z pliku od znajomego; przy kolizji nazwy dopisuje „(2)”.</summary>
    public static Theme Import(string path)
    {
        var t = JsonSerializer.Deserialize<Theme>(File.ReadAllText(path)) ?? throw new InvalidDataException(L.T("Pusty plik motywu."));
        if (!t.IsValid()) throw new InvalidDataException(L.T("Plik motywu ma niepoprawne kolory."));
        string baseName = string.IsNullOrWhiteSpace(t.Name) ? Path.GetFileNameWithoutExtension(path) : t.Name;
        string name = baseName;
        for (int i = 2; All.Any(x => x.Name == name); i++) name = $"{baseName} ({i})";
        t = t with { Name = name };
        t.SaveCustom();
        return t;
    }

    /// <summary>Wszystkie pola kolorów (do edytora).</summary>
    public static readonly (string Prop, string Label)[] ColorFields =
    {
        ("Panel", "Tło panelu"), ("PanelBorder", "Ramka panelu"), ("Header", "Nazwa kanału"),
        ("Text", "Nick (cisza)"), ("TextTalking", "Nick (mówi)"), ("Talk", "Znacznik: mówi"),
        ("Whisper", "Znacznik: szept"), ("Idle", "Znacznik: cisza"), ("TalkRow", "Tło wiersza mówiącego"),
        ("Join", "Powiadomienie: wejście"), ("Leave", "Powiadomienie: wyjście"), ("Info", "Powiadomienie: info"),
        ("Message", "Wiadomość"), ("Poke", "Szturchnięcie"), ("NoticeText", "Tekst powiadomień"),
        ("Banner", "Pasek: mikrofon wyciszony"), ("Muted", "Ikony wyciszenia"), ("Accent", "Ramka przy przesuwaniu"),
    };

    public string GetColor(string prop) => (string)typeof(Theme).GetProperty(prop)!.GetValue(this)!;

    public Theme WithColor(string prop, string hex)
    {
        var copy = this with { };
        typeof(Theme).GetProperty(prop)!.SetValue(copy, hex);
        return copy;
    }

    bool IsValid()
    {
        try
        {
            foreach (var (prop, _) in ColorFields) C(GetColor(prop));
            return true;
        }
        catch { return false; }
    }
}
