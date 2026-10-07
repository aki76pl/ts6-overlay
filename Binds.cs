using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace TS6Overlay;

/// <summary>Bind: skrót klawiszowy, który odtwarza wybrany dźwięk (soundboard).</summary>
public sealed class SoundBind
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Nowy bind";
    /// <summary>Modyfikatory jak w RegisterHotKey: 1 Alt, 2 Ctrl, 4 Shift, 8 Win.</summary>
    public uint Modifiers { get; set; }
    /// <summary>Kod klawisza (virtual-key); 0 = nieprzypisany.</summary>
    public int Key { get; set; }
    public string SoundPath { get; set; } = "";
    public int Volume { get; set; } = 80;
    public bool Enabled { get; set; } = true;

    public string KeyText => Key == 0 ? L.T("— brak —") : Format(Modifiers, Key);

    public static string Format(uint mods, int vk)
    {
        var parts = new List<string>();
        if ((mods & 2) != 0) parts.Add("Ctrl");
        if ((mods & 1) != 0) parts.Add("Alt");
        if ((mods & 4) != 0) parts.Add("Shift");
        if ((mods & 8) != 0) parts.Add("Win");
        var k = KeyInterop.KeyFromVirtualKey(vk);
        string name = k switch
        {
            >= System.Windows.Input.Key.D0 and <= System.Windows.Input.Key.D9 => ((int)(k - System.Windows.Input.Key.D0)).ToString(),
            >= System.Windows.Input.Key.NumPad0 and <= System.Windows.Input.Key.NumPad9 => "Num " + (int)(k - System.Windows.Input.Key.NumPad0),
            System.Windows.Input.Key.Multiply => "Num *",
            System.Windows.Input.Key.Add => "Num +",
            System.Windows.Input.Key.Subtract => "Num -",
            System.Windows.Input.Key.Divide => "Num /",
            System.Windows.Input.Key.Decimal => "Num ,",
            System.Windows.Input.Key.OemTilde => "`",
            System.Windows.Input.Key.OemMinus => "-",
            System.Windows.Input.Key.OemPlus => "=",
            System.Windows.Input.Key.OemOpenBrackets => "[",
            System.Windows.Input.Key.OemCloseBrackets => "]",
            System.Windows.Input.Key.OemPipe => "\\",
            System.Windows.Input.Key.OemSemicolon => ";",
            System.Windows.Input.Key.OemQuotes => "'",
            System.Windows.Input.Key.OemComma => ",",
            System.Windows.Input.Key.OemPeriod => ".",
            System.Windows.Input.Key.OemQuestion => "/",
            _ => k.ToString(),
        };
        parts.Add(name);
        return string.Join("+", parts);
    }
}

/// <summary>
/// Globalne skróty (RegisterHotKey — działają też w grze) i odtwarzanie dźwięków bindów.
/// Dźwięk może iść na wybrane urządzenie (np. wirtualny kabel do mikrofonu, żeby słyszeli inni) i/lub do Ciebie.
/// </summary>
public sealed class BindManager : IDisposable
{
    const int WM_HOTKEY = 0x0312, BaseId = 0x6100;
    const uint MOD_NOREPEAT = 0x4000;
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    readonly HwndSource _wnd;
    readonly Config _cfg;
    readonly Dictionary<int, SoundBind> _registered = new();
    readonly List<WaveOutEvent> _playing = new();
    string? _playingId;
    /// <summary>Id binda, który właśnie gra (null = cisza) — dla panelu na telefonie.</summary>
    public string? PlayingId => _playing.Any(o => o.PlaybackState != PlaybackState.Stopped) ? _playingId : null;

    /// <summary>Bindy, których nie udało się zarejestrować (skrót zajęty przez inny program).</summary>
    public HashSet<string> Failed { get; } = new();
    public event Action? Changed;
    /// <summary>Naciśnięto klawisz podglądu (serwer albo wiadomości) — App sprawdza, kiedy zostanie puszczony.</summary>
    public event Action<SoundBind>? PeekPressed;

    public BindManager(Config cfg)
    {
        _cfg = cfg;
        _wnd = new HwndSource(new HwndSourceParameters("TS6OverlayBinds") { Width = 0, Height = 0, ParentWindow = new IntPtr(-3) });
        _wnd.AddHook(WndProc);
    }

    /// <summary>Rejestruje skróty od nowa według konfiguracji.</summary>
    public void Register()
    {
        UnregisterAll();
        Failed.Clear();
        // Skróty specjalne mają własne id, przed bindami.
        int special = BaseId - 1;
        foreach (var sb in new[] { _cfg.StopBind, _cfg.PeekServerBind, _cfg.PeekMessagesBind })
        {
            if (sb.Key != 0)
            {
                if (RegisterHotKey(_wnd.Handle, special, sb.Modifiers | MOD_NOREPEAT, (uint)sb.Key)) _registered[special] = sb;
                else Failed.Add(sb.Id);
            }
            special--;
        }
        int id = BaseId;
        foreach (var b in _cfg.Binds)
        {
            if (!b.Enabled || b.Key == 0) continue;
            if (RegisterHotKey(_wnd.Handle, id, b.Modifiers | MOD_NOREPEAT, (uint)b.Key)) _registered[id] = b;
            else Failed.Add(b.Id);
            id++;
        }
        Changed?.Invoke();
    }

    /// <summary>Na czas nagrywania skrótu w ustawieniach — żeby klawisz nie odpalał binda.</summary>
    public void UnregisterAll()
    {
        foreach (var id in _registered.Keys) UnregisterHotKey(_wnd.Handle, id);
        _registered.Clear();
    }

    IntPtr WndProc(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == WM_HOTKEY && _registered.TryGetValue(w.ToInt32(), out var b))
        {
            if (b == _cfg.StopBind) StopAll();
            else if (b == _cfg.PeekServerBind || b == _cfg.PeekMessagesBind) PeekPressed?.Invoke(b);
            else Trigger(b);
            handled = true;
        }
        return IntPtr.Zero;
    }

    /// <summary>Odtwarza dźwięk binda; ponowne naciśnięcie w trakcie zatrzymuje go.</summary>
    public void Trigger(SoundBind b)
    {
        bool same = _playingId == b.Id && _playing.Count > 0;
        Stop();
        if (same) return;
        if (string.IsNullOrEmpty(b.SoundPath) || !System.IO.File.Exists(b.SoundPath)) return;

        var devices = new List<int>();
        if (_cfg.BindDeviceName != "")
        {
            var dev = OutputDevices().FirstOrDefault(d => d.name == _cfg.BindDeviceName);
            if (dev.name != null) devices.Add(dev.id);
        }
        if (devices.Count == 0 || _cfg.BindAlsoLocal) devices.Add(-1);   // -1 = domyślne urządzenie (Ty)

        foreach (var dev in devices.Distinct())
        {
            try
            {
                var reader = new AudioFileReader(b.SoundPath);
                var vol = new VolumeSampleProvider(reader) { Volume = Math.Clamp(b.Volume, 0, 100) / 100f };
                var o = new WaveOutEvent { DeviceNumber = dev };
                o.Init(vol);
                o.PlaybackStopped += (_, _) => { reader.Dispose(); };
                o.Play();
                _playing.Add(o);
            }
            catch { }
        }
        _playingId = b.Id;
    }

    /// <summary>Zatrzymuje wszystko: dźwięki bindów i bieżący sygnał powiadomienia.</summary>
    public void StopAll()
    {
        Stop();
        Sounds.Stop();
        StoppedAll?.Invoke();
    }

    /// <summary>Np. żeby uciszyć też lektora.</summary>
    public event Action? StoppedAll;

    public void Stop()
    {
        foreach (var o in _playing)
        {
            try { o.Stop(); o.Dispose(); } catch { }
        }
        _playing.Clear();
        _playingId = null;
    }

    /// <summary>Urządzenia wyjściowe audio (WinMM) — nazwy bywają ucięte do 31 znaków.</summary>
    public static List<(int id, string name)> OutputDevices()
    {
        var list = new List<(int, string)>();
        for (int i = 0; i < WaveOut.DeviceCount; i++)
        {
            try { list.Add((i, WaveOut.GetCapabilities(i).ProductName)); } catch { }
        }
        return list;
    }

    public void Dispose()
    {
        Stop();
        UnregisterAll();
        _wnd.Dispose();
    }
}
