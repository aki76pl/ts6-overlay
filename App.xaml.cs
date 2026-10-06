using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace TS6Overlay;

public partial class App : Application
{
    public Config Cfg { get; private set; } = null!;
    public TsClient Ts { get; private set; } = null!;
    public Stats Stats { get; } = new();
    public MicMonitor Mic { get; } = new();
    public ObsServer Obs { get; } = new();
    public Updater.Release? PendingUpdate { get; private set; }
    public BindManager Binds { get; private set; } = null!;

    OverlayWindow _win = null!;
    Forms.NotifyIcon _tray = null!;
    readonly CancellationTokenSource _stop = new();
    HwndSource? _hotkeySrc;
    Mutex? _single;
    SettingsWindow? _settings;
    StatsWindow? _statsWin;
    OverlayState _state = OverlayState.Disconnected;
    bool _tsRunning, _userHidden, _gameActive, _micTest;
    DateTime _lastMuteBeep = DateTime.MinValue;
    Theme? _previewTheme;

    const int HOTKEY_ID = 0x5A61;
    const uint MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, VK_O = 0x4F;
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public static new App Current => (App)Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;
        if (args.Length == 2 && args[0] == "--preview") { Theme.LoadCustom(); RenderPreviews(args[1]); Shutdown(); return; }
        if (args.Contains("--uninstall"))
        {
            if (MessageBox.Show("Odinstalować TS6 Overlay?\n\nUstawienia i motywy w %APPDATA%\\TS6Overlay zostaną zachowane.",
                    "TS6 Overlay", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                foreach (var p in Process.GetProcessesByName("TS6Overlay"))
                    if (p.Id != Environment.ProcessId) try { p.Kill(); } catch { }
                Installer.Uninstall();
                MessageBox.Show("TS6 Overlay został odinstalowany.", "TS6 Overlay");
            }
            Shutdown();
            return;
        }

        // Po aktualizacji/instalacji poprzednia kopia jeszcze się zamyka — poczekaj na nią.
        bool handover = args.Contains("--updated") || args.Contains("--installed");
        _single = new Mutex(false, "TS6Overlay.single");
        bool owned;
        try { owned = _single.WaitOne(handover ? 15000 : 0); }
        catch (AbandonedMutexException) { owned = true; }
        if (!owned)
        {
            // Program już działa — poproś go o otwarcie ustawień (np. kliknięcie skrótu w menu Start).
            try { if (EventWaitHandle.TryOpenExisting(ShowSettingsEvent, out var ev)) ev.Set(); } catch { }
            Shutdown();
            return;
        }

        Updater.Cleanup();
        Cfg = Config.Load();
        Theme.LoadCustom();
        Sounds.Volume = Cfg.Volume;
        Sounds.Files = new(Cfg.SoundFiles);

        if (args.Contains("--updated")) Installer.RefreshVersion();
        else if (!handover && OfferInstall()) { Shutdown(); return; }

        _win = new OverlayWindow(Cfg);
        _tsRunning = IsTeamSpeakRunning();

        Ts = new TsClient(Cfg);
        Ts.StateChanged += () => Dispatcher.BeginInvoke(Refresh);
        Ts.Notice += n => Dispatcher.BeginInvoke(() => OnNotice(n));
        Ts.TalkChanged += (c, t) => Dispatcher.BeginInvoke(() => Stats.OnTalk(c, t));
        Ts.Status += s => Dispatcher.BeginInvoke(() => SetStatus(s));
        _ = Task.Run(() => Ts.RunAsync(_stop.Token));

        Mic.VoiceChanged += v => Dispatcher.BeginInvoke(() => OnVoiceWhileMuted(v));

        Binds = new BindManager(Cfg);
        BuildTray();
        RegisterToggleHotkey();
        ListenForSecondInstance();
        ApplySettings();

        // Co sekundę: czy działa TeamSpeak, czy na pierwszym planie jest gra.
        var watch = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        watch.Tick += (_, _) => WatchTick();
        watch.Start();

        if (args.Contains("--updated"))
            _tray.ShowBalloonTip(4000, "TS6 Overlay", $"Zaktualizowano do wersji {Updater.Current}.", Forms.ToolTipIcon.Info);
        if (Cfg.AutoUpdateCheck)
            _ = Task.Delay(TimeSpan.FromSeconds(15)).ContinueWith(_ => Dispatcher.BeginInvoke(() => CheckForUpdates(false)));
    }

    const string ShowSettingsEvent = "TS6Overlay.showSettings";

    void ListenForSecondInstance()
    {
        var ev = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEvent);
        var t = new Thread(() =>
        {
            while (!_stop.IsCancellationRequested)
                if (ev.WaitOne(1000)) Dispatcher.BeginInvoke(() => OpenSettings());
        }) { IsBackground = true, Name = "second-instance" };
        t.Start();
        if (Environment.GetCommandLineArgs().Contains("--settings")) Dispatcher.BeginInvoke(() => OpenSettings());
        if (Environment.GetCommandLineArgs().Contains("--stats")) Dispatcher.BeginInvoke(OpenStats);
    }

    // ---------- instalacja ----------

    bool OfferInstall()
    {
        if (Installer.IsInstalledCopy || Cfg.InstallPromptShown) return false;
        Cfg.InstallPromptShown = true;
        Cfg.Save();
        if (Installer.IsInstalled) return false;
        var r = MessageBox.Show(
            "Zainstalować TS6 Overlay na tym komputerze?\n\n" +
            "• program trafi do %LOCALAPPDATA%\\Programs\\TS6Overlay,\n" +
            "• pojawi się skrót w menu Start,\n" +
            "• nakładka będzie startować z Windows (i pokazywać się razem z TeamSpeakiem),\n" +
            "• odinstalujesz go w Ustawieniach Windows → Aplikacje.\n\n" +
            "„Nie” = uruchom bez instalacji (możesz zainstalować później w Ustawieniach → Aktualizacje).",
            "TS6 Overlay", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return false;
        try
        {
            _single?.ReleaseMutex();
            Installer.Install();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Instalacja nie powiodła się: " + ex.Message, "TS6 Overlay");
            return false;
        }
    }

    public void InstallNow()
    {
        try
        {
            Quit(beforeShutdown: () => { _single?.ReleaseMutex(); Installer.Install(); });
        }
        catch (Exception ex) { MessageBox.Show("Instalacja nie powiodła się: " + ex.Message, "TS6 Overlay"); }
    }

    // ---------- stan i zdarzenia ----------

    void Refresh()
    {
        _state = Ts.Snapshot();
        _win.View.Render(_state);
        Obs.Update(_state, _win.View.Theme, Cfg);
        UpdateMic();
    }

    void OnNotice(Notice n)
    {
        _win.View.AddNotice(n);
        Obs.AddNotice(n);
        Obs.Update(_state, _win.View.Theme, Cfg);
        Stats.OnNotice(n);

        var fav = n.Who != null ? Cfg.FavoriteFor(n.Who.Uid) : null;
        switch (n.Kind)
        {
            case NoticeKind.Join when Cfg.SoundOnJoin:
                if (fav != null) Sounds.Play(SoundKind.Favorite, fav.SoundPath);
                else Sounds.Play(SoundKind.Join);
                break;
            case NoticeKind.Leave when Cfg.SoundOnLeave: Sounds.Play(SoundKind.Leave); break;
            case NoticeKind.Message when Cfg.SoundOnMessage: Sounds.Play(SoundKind.Message); break;
            case NoticeKind.Poke when Cfg.SoundOnPoke: Sounds.Play(SoundKind.Poke); break;
        }
    }

    // ---------- mikrofon wyciszony ----------

    /// <summary>Test mikrofonu w ustawieniach (podgląd poziomu).</summary>
    public bool MicTest
    {
        get => _micTest;
        set { _micTest = value; UpdateMic(); }
    }

    void UpdateMic()
    {
        bool need = _micTest || (Cfg.MuteWarning && Cfg.MuteVoiceDetect && _state.MyInputMuted && _state.Channel != null);
        Mic.Threshold = Math.Clamp(Cfg.MuteSensitivity, 1, 100) / 100f;
        if (need && !Mic.Running) Mic.Start();
        else if (!need && Mic.Running) Mic.Stop();
    }

    void OnVoiceWhileMuted(bool v)
    {
        bool real = v && _state.MyInputMuted;
        _win.View.SetVoiceWhileMuted(real);
        if (real && Cfg.MuteWarningSound && (DateTime.Now - _lastMuteBeep).TotalSeconds > 4)
        {
            _lastMuteBeep = DateTime.Now;
            Sounds.Play(SoundKind.MuteWarning);
        }
    }

    // ---------- widoczność: TeamSpeak, gry, skrót ----------

    void WatchTick()
    {
        bool changed = false;
        bool running = IsTeamSpeakRunning();
        if (running != _tsRunning) { _tsRunning = running; changed = true; }
        if (Cfg.GameOnly)
        {
            var g = GameDetector.IsGameInForeground(Cfg);
            if (g.HasValue && g.Value != _gameActive) { _gameActive = g.Value; changed = true; }
        }
        if (changed) UpdateVisibility();
    }

    static bool IsTeamSpeakRunning()
    {
        var p = Process.GetProcessesByName("TeamSpeak");
        foreach (var x in p) x.Dispose();
        return p.Length > 0;
    }

    void UpdateVisibility()
    {
        bool show = _tsRunning && !_userHidden && (!Cfg.GameOnly || _gameActive || _win.EditMode);
        if (show && !_win.IsVisible) _win.Show();
        else if (!show && _win.IsVisible) _win.Hide();
    }

    void ToggleVisible()
    {
        _userHidden = !_userHidden;
        UpdateVisibility();
    }

    // ---------- ustawienia ----------

    /// <summary>Zastosuj bieżącą konfigurację (wywoływane przez okno ustawień przy każdej zmianie).</summary>
    public void ApplySettings()
    {
        Cfg.Save();
        Sounds.Volume = Cfg.Volume;
        Sounds.Files = new(Cfg.SoundFiles);
        if (!BindCapture) Binds.Register();
        _win.ApplyScale();
        _win.View.ApplyTheme(_previewTheme ?? Theme.ByName(Cfg.Theme));
        _win.View.ResetIdle();
        if (Cfg.ObsEnabled && (Obs.Port != Cfg.ObsPort || !ObsRunning)) ObsRunning = Obs.Start(Cfg.ObsPort);
        else if (!Cfg.ObsEnabled && ObsRunning) { Obs.Stop(); ObsRunning = false; }
        if (!Cfg.GameOnly) _gameActive = false;
        WatchTick();
        UpdateVisibility();
        Refresh();
    }

    public bool ObsRunning { get; private set; }

    /// <summary>Ustawienia nagrywają skrót — wyłącz bindy, żeby naciśnięty klawisz ich nie odpalił.</summary>
    public bool BindCapture
    {
        get => _bindCapture;
        set { _bindCapture = value; if (value) Binds.UnregisterAll(); else Binds.Register(); }
    }
    bool _bindCapture;

    /// <summary>Podgląd motywu z edytora na żywej nakładce (null = wróć do wybranego).</summary>
    public void PreviewTheme(Theme? t)
    {
        _previewTheme = t;
        _win.View.ApplyTheme(t ?? Theme.ByName(Cfg.Theme));
        Obs.Update(_state, _win.View.Theme, Cfg);
    }

    void OpenSettings(int tab = 0)
    {
        if (_settings == null)
        {
            _settings = new SettingsWindow(this);
            _settings.Closed += (_, _) => { _settings = null; PreviewTheme(null); MicTest = false; };
            _settings.Show();
        }
        _settings.SelectTab(tab);
        _settings.Activate();
    }

    void OpenStats()
    {
        if (_statsWin == null)
        {
            _statsWin = new StatsWindow(Stats);
            _statsWin.Closed += (_, _) => _statsWin = null;
            _statsWin.Show();
        }
        _statsWin.Activate();
    }

    // ---------- aktualizacje ----------

    public async void CheckForUpdates(bool interactive)
    {
        try
        {
            var r = await Updater.CheckAsync();
            PendingUpdate = r;
            if (r == null)
            {
                if (interactive) MessageBox.Show($"Masz najnowszą wersję ({Updater.Current}).", "TS6 Overlay");
                return;
            }
            if (!interactive && r.Tag == Cfg.SkippedVersion) return;
            if (interactive) await AskAndUpdate(r);
            else _tray.ShowBalloonTip(8000, "TS6 Overlay — aktualizacja",
                $"Dostępna wersja {r.Version}. Kliknij, aby zaktualizować.", Forms.ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            if (interactive) MessageBox.Show("Nie udało się sprawdzić aktualizacji: " + ex.Message, "TS6 Overlay");
        }
    }

    public async Task AskAndUpdate(Updater.Release r)
    {
        var notes = r.Notes.Length > 600 ? r.Notes[..600] + "…" : r.Notes;
        var ans = MessageBox.Show($"Dostępna wersja {r.Version} (masz {Updater.Current}).\n\n{notes}\n\nZaktualizować teraz?",
            "TS6 Overlay — aktualizacja", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (ans == MessageBoxResult.No) { Cfg.SkippedVersion = r.Tag; Cfg.Save(); return; }
        if (ans != MessageBoxResult.Yes) return;
        try
        {
            SetStatus("Pobieranie aktualizacji…");
            await Updater.InstallAsync(r);
            Quit(beforeShutdown: () => _single?.ReleaseMutex());
        }
        catch (Exception ex)
        {
            MessageBox.Show("Aktualizacja nie powiodła się: " + ex.Message, "TS6 Overlay");
        }
    }

    // ---------- zasobnik ----------

    void SetStatus(string s)
    {
        var t = "TS6 Overlay — " + s;
        _tray.Text = t.Length > 63 ? t[..63] : t;
    }

    void BuildTray()
    {
        Forms.ContextMenuStrip menu = new();
        menu.Opening += (_, _) => FillMenu(menu);
        FillMenu(menu);

        System.Drawing.Icon icon;
        try { icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Information; }
        catch { icon = System.Drawing.SystemIcons.Information; }

        _tray = new Forms.NotifyIcon { Icon = icon, Text = "TS6 Overlay", Visible = true, ContextMenuStrip = menu };
        _tray.DoubleClick += (_, _) => OpenSettings();
        _tray.BalloonTipClicked += async (_, _) => { if (PendingUpdate != null) await AskAndUpdate(PendingUpdate); };
    }

    /// <summary>Menu budowane przy każdym otwarciu — zawsze zgodne z oknem ustawień.</summary>
    void FillMenu(Forms.ContextMenuStrip menu)
    {
        menu.Items.Clear();
        Forms.ToolStripMenuItem Item(string text, Action click, bool? check = null)
        {
            var it = new Forms.ToolStripMenuItem(text);
            if (check.HasValue) it.Checked = check.Value;
            it.Click += (_, _) => click();
            return it;
        }

        var settings = Item("Ustawienia…", () => OpenSettings());
        settings.Font = new System.Drawing.Font(settings.Font, System.Drawing.FontStyle.Bold);
        menu.Items.Add(settings);
        menu.Items.Add(Item("Statystyki sesji…", OpenStats));
        menu.Items.Add(new Forms.ToolStripSeparator());

        menu.Items.Add(Item((_userHidden ? "Pokaż" : "Ukryj") + " nakładkę  (Ctrl+Shift+O)", ToggleVisible));
        menu.Items.Add(Item(_win.EditMode ? "Zablokuj pozycję" : "Przesuń nakładkę", () =>
        {
            _win.EditMode = !_win.EditMode;
            UpdateVisibility();
        }));

        var theme = new Forms.ToolStripMenuItem("Motyw");
        foreach (var t in Theme.All)
            theme.DropDownItems.Add(Item(t.Name, () =>
            {
                Cfg.Theme = t.Name;
                ApplySettings();
                _win.View.AddNotice(new($"Motyw: {t.Name}", NoticeKind.Info));
            }, Theme.ByName(Cfg.Theme).Name == t.Name));
        menu.Items.Add(theme);

        menu.Items.Add(Item("Pokazuj wszystkich na kanale", () => { Cfg.ShowChannelList = !Cfg.ShowChannelList; ApplySettings(); }, Cfg.ShowChannelList));
        menu.Items.Add(Item("Autoukrywanie, gdy nikt nie mówi", () => { Cfg.AutoHide = !Cfg.AutoHide; ApplySettings(); }, Cfg.AutoHide));
        menu.Items.Add(Item("Pokazuj tylko w grach", () =>
        {
            Cfg.GameOnly = !Cfg.GameOnly;
            if (Cfg.GameOnly && Cfg.Games.Count == 0 && !Cfg.GameFullscreenAny)
                OpenSettings(SettingsWindow.TabBehavior);
            ApplySettings();
        }, Cfg.GameOnly));

        var sound = new Forms.ToolStripMenuItem("Dźwięki");
        sound.DropDownItems.Add(Item("Gdy ktoś wchodzi na kanał", () => { Cfg.SoundOnJoin = !Cfg.SoundOnJoin; ApplySettings(); if (Cfg.SoundOnJoin) Sounds.Play(SoundKind.Join); }, Cfg.SoundOnJoin));
        sound.DropDownItems.Add(Item("Gdy ktoś wychodzi z kanału", () => { Cfg.SoundOnLeave = !Cfg.SoundOnLeave; ApplySettings(); if (Cfg.SoundOnLeave) Sounds.Play(SoundKind.Leave); }, Cfg.SoundOnLeave));
        sound.DropDownItems.Add(Item("Wiadomości", () => { Cfg.SoundOnMessage = !Cfg.SoundOnMessage; ApplySettings(); if (Cfg.SoundOnMessage) Sounds.Play(SoundKind.Message); }, Cfg.SoundOnMessage));
        sound.DropDownItems.Add(Item("Szturchnięcia", () => { Cfg.SoundOnPoke = !Cfg.SoundOnPoke; ApplySettings(); if (Cfg.SoundOnPoke) Sounds.Play(SoundKind.Poke); }, Cfg.SoundOnPoke));
        menu.Items.Add(sound);

        var binds = new Forms.ToolStripMenuItem("Bindy (soundboard)");
        foreach (var b in Cfg.Binds.Where(x => x.Enabled && x.SoundPath != ""))
            binds.DropDownItems.Add(Item($"{b.Name}   [{b.KeyText}]", () => Binds.Trigger(b)));
        if (binds.DropDownItems.Count > 0) binds.DropDownItems.Add(new Forms.ToolStripSeparator());
        binds.DropDownItems.Add(Item("Zatrzymaj wszystkie dźwięki" + (Cfg.StopBind.Key != 0 ? $"   [{Cfg.StopBind.KeyText}]" : ""), () => Binds.StopAll()));
        binds.DropDownItems.Add(Item("Ustaw bindy…", () => OpenSettings(SettingsWindow.TabBinds)));
        menu.Items.Add(binds);

        menu.Items.Add(Item("Nakładka dla OBS" + (ObsRunning ? $"  ({Obs.Url})" : ""), () => OpenSettings(SettingsWindow.TabObs), Cfg.ObsEnabled));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Item(PendingUpdate != null ? $"Zaktualizuj do {PendingUpdate.Version}…" : "Sprawdź aktualizacje", () => CheckForUpdates(true)));
        menu.Items.Add(Item("Zamknij", () => Quit()));
    }

    void RegisterToggleHotkey()
    {
        var p = new HwndSourceParameters("TS6OverlayHotkey") { Width = 0, Height = 0, ParentWindow = new IntPtr(-3) /* HWND_MESSAGE */ };
        _hotkeySrc = new HwndSource(p);
        _hotkeySrc.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (msg == 0x0312 && w.ToInt32() == HOTKEY_ID) { ToggleVisible(); handled = true; }
            return IntPtr.Zero;
        });
        RegisterHotKey(_hotkeySrc.Handle, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_O);
    }

    void Quit(Action? beforeShutdown = null)
    {
        _stop.Cancel();
        Mic.Dispose();
        Obs.Dispose();
        Binds?.Dispose();
        if (_hotkeySrc != null) { UnregisterHotKey(_hotkeySrc.Handle, HOTKEY_ID); _hotkeySrc.Dispose(); }
        _tray.Visible = false;
        _tray.Dispose();
        if (_win.EditMode) _win.EditMode = false;
        Cfg.Save();
        beforeShutdown?.Invoke();
        Shutdown();
    }

    /// <summary>Zapisuje podgląd każdego motywu do PNG (przykładowe dane).</summary>
    static void RenderPreviews(string dir)
    {
        System.IO.Directory.CreateDirectory(dir);
        foreach (var t in Theme.All)
        {
            var cfg = new Config { Theme = t.Name, Opacity = 1 };
            var w = new OverlayWindow(cfg) { Left = -5000 };
            w.Show();
            w.View.ShowDemo();
            w.View.Render(OverlayView.DemoState(cfg) with { MyInputMuted = true });
            w.UpdateLayout();
            var el = (FrameworkElement)w.Content;
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)Math.Ceiling(el.ActualWidth), (int)Math.Ceiling(el.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bmp.Render(el);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var f = System.IO.File.Create(System.IO.Path.Combine(dir, t.Name.Split(' ')[0] + ".png"));
            enc.Save(f);
            w.Close();
        }
    }
}
