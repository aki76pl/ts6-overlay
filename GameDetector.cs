using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TS6Overlay;

/// <summary>Sprawdza, czy na pierwszym planie jest gra (z listy albo aplikacja pełnoekranowa).</summary>
public static class GameDetector
{
    static readonly HashSet<string> NeverGame = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "TeamSpeak", "TS6Overlay", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost",
        "LockApp", "ApplicationFrameHost", "TextInputHost",
        // przeglądarki i odtwarzacze na pełnym ekranie to nie gry
        "chrome", "msedge", "firefox", "opera", "opera_gx", "brave", "vivaldi", "vlc", "mpc-hc64", "mpc-be64", "PotPlayerMini64",
    };

    /// <summary>Nazwa procesu okna na pierwszym planie (bez .exe) albo "".</summary>
    public static string ForegroundProcess()
    {
        try
        {
            var h = GetForegroundWindow();
            if (h == IntPtr.Zero) return "";
            GetWindowThreadProcessId(h, out uint pid);
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch { return ""; }
    }

    /// <summary>true = gra, false = nie gra, null = okno neutralne (TS, nakładka) — zostaw jak było.</summary>
    public static bool? IsGameInForeground(Config cfg)
    {
        string name = ForegroundProcess();
        if (name == "") return false;
        if (name.Equals("TeamSpeak", StringComparison.OrdinalIgnoreCase) || name.Equals("TS6Overlay", StringComparison.OrdinalIgnoreCase))
            return null;
        if (cfg.Games.Concat(cfg.Profiles.Select(p => p.Game)).Any(g => Normalize(g).Equals(name, StringComparison.OrdinalIgnoreCase))) return true;
        if (cfg.GameFullscreenAny && !NeverGame.Contains(name) && IsForegroundFullscreen()) return true;
        return false;
    }

    public static string Normalize(string exe)
    {
        exe = exe.Trim();
        return exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exe[..^4] : exe;
    }

    /// <summary>Programy z oknami — do szybkiego dodawania gier w ustawieniach.</summary>
    public static List<string> WindowedProcesses()
    {
        var list = new List<string>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(p.MainWindowTitle) && !NeverGame.Contains(p.ProcessName))
                    list.Add(p.ProcessName);
            }
            catch { }
            finally { p.Dispose(); }
        }
        return list.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static bool IsForegroundFullscreen()
    {
        var h = GetForegroundWindow();
        if (h == IntPtr.Zero || h == GetShellWindow() || h == GetDesktopWindow()) return false;
        if (!GetWindowRect(h, out var r)) return false;
        var mon = MonitorFromWindow(h, 2 /* MONITOR_DEFAULTTONEAREST */);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(mon, ref mi)) return false;
        var m = mi.rcMonitor;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }

    /// <summary>
    /// true = na pierwszym planie działa aplikacja Direct3D w trybie wyłącznego pełnego ekranu —
    /// wtedy żadne okno (także nakładka) nie może wyświetlić się nad grą.
    /// </summary>
    public static bool IsExclusiveFullscreen()
    {
        try { return SHQueryUserNotificationState(out int state) == 0 && state == 3; /* QUNS_RUNNING_D3D_FULL_SCREEN */ }
        catch { return false; }
    }

    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFO mi);
}
