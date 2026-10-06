using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace TS6Overlay;

/// <summary>
/// Wbudowany instalator: kopiuje program do %LOCALAPPDATA%\Programs\TS6Overlay, tworzy skrót w menu Start,
/// włącza autostart i dodaje wpis „Aplikacje → Zainstalowane aplikacje” (odinstalowanie przez --uninstall).
/// Bez uprawnień administratora — wszystko w profilu użytkownika.
/// </summary>
public static class Installer
{
    public static string InstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "TS6Overlay");
    public static string InstalledExe => Path.Combine(InstallDir, "TS6Overlay.exe");
    static string StartMenuLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "TS6 Overlay.lnk");
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\TS6Overlay";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsInstalledCopy =>
        string.Equals(Path.GetFullPath(Environment.ProcessPath ?? ""), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

    public static bool IsInstalled => File.Exists(InstalledExe);

    public static bool AutostartEnabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue("TS6Overlay") != null;
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) k.SetValue("TS6Overlay", "\"" + Environment.ProcessPath + "\"");
            else k.DeleteValue("TS6Overlay", false);
        }
    }

    /// <summary>Instaluje bieżący plik i uruchamia zainstalowaną kopię. Wywołujący zamyka się potem.</summary>
    public static void Install()
    {
        Directory.CreateDirectory(InstallDir);
        File.Copy(Environment.ProcessPath!, InstalledExe, true);

        CreateShortcut(StartMenuLink, InstalledExe);

        using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            k.SetValue("TS6Overlay", "\"" + InstalledExe + "\"");

        using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            k.SetValue("DisplayName", "TS6 Overlay");
            k.SetValue("DisplayVersion", Updater.Current.ToString());
            k.SetValue("Publisher", "TS6 Overlay");
            k.SetValue("DisplayIcon", InstalledExe);
            k.SetValue("InstallLocation", InstallDir);
            k.SetValue("UninstallString", "\"" + InstalledExe + "\" --uninstall");
            k.SetValue("URLInfoAbout", Updater.ReleasesPage);
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            k.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
        }

        Process.Start(new ProcessStartInfo(InstalledExe, "--installed") { UseShellExecute = true });
    }

    /// <summary>Usuwa skróty, wpisy rejestru i (po zamknięciu programu) folder instalacji. Ustawienia zostają.</summary>
    public static void Uninstall()
    {
        try { File.Delete(StartMenuLink); } catch { }
        try { Registry.CurrentUser.DeleteSubKey(UninstallKey, false); } catch { }
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            k.DeleteValue("TS6Overlay", false);
        }
        catch { }
        // Folder usuwa cmd po chwili, gdy ten proces już się zamknie.
        Process.Start(new ProcessStartInfo("cmd.exe",
            $"/c timeout /t 2 /nobreak >nul & rmdir /s /q \"{InstallDir}\"")
        { CreateNoWindow = true, UseShellExecute = false });
    }

    /// <summary>Po aktualizacji zapisuje nową wersję we wpisie odinstalowania.</summary>
    public static void RefreshVersion()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(UninstallKey, true);
            k?.SetValue("DisplayVersion", Updater.Current.ToString());
        }
        catch { }
    }

    static void CreateShortcut(string link, string target)
    {
        var t = Type.GetTypeFromProgID("WScript.Shell");
        if (t == null) return;
        dynamic shell = Activator.CreateInstance(t)!;
        var sc = shell.CreateShortcut(link);
        sc.TargetPath = target;
        sc.WorkingDirectory = Path.GetDirectoryName(target);
        sc.Description = "Nakładka TeamSpeak 6: kto mówi i kto wchodzi na kanał";
        sc.IconLocation = target + ",0";
        sc.Save();
    }
}
