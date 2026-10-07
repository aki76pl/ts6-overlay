using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using static TS6Overlay.L;
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
    public Speech Voice { get; private set; } = null!;
    public LongTermStats LongTerm { get; } = new();
    public DiscordPresence Discord { get; private set; } = null!;
    public PhoneServer Phone { get; private set; } = null!;
    public Rgb Lights { get; private set; } = null!;
    volatile string _phoneJson = "{}";
    /// <summary>Profil gry, który jest teraz zastosowany (null = ustawienia główne).</summary>
    public GameProfile? ActiveProfile { get; private set; }

    OverlayWindow _win = null!;
    Forms.NotifyIcon _tray = null!;
    readonly CancellationTokenSource _stop = new();
    HwndSource? _hotkeySrc;
    Mutex? _single;
    SettingsWindow? _settings;
    StatsWindow? _statsWin;
    OverlayState _state = OverlayState.Disconnected;
    bool _tsRunning, _userHidden, _gameActive, _micTest, _peekForce;
    DateTime _lastMuteBeep = DateTime.MinValue, _lastMuteFlash = DateTime.MinValue;
    Theme? _previewTheme;
    readonly System.Windows.Threading.DispatcherTimer _peekTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    SoundBind? _peekBind;

    const int HOTKEY_ID = 0x5A61;
    const uint MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, VK_O = 0x4F;
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);

    public static new App Current => (App)Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;
        Cfg = Config.Load();
        L.Set(Cfg.Language);

        if (args.Length == 2 && args[0] == "--preview") { Theme.LoadCustom(); RenderPreviews(args[1]); Shutdown(); return; }
        if (args.Contains("--uninstall"))
        {
            if (MessageBox.Show(T("Odinstalować TS6 Overlay?\n\nUstawienia i motywy w %APPDATA%\\TS6Overlay zostaną zachowane."),
                    "TS6 Overlay", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                foreach (var p in Process.GetProcessesByName("TS6Overlay"))
                    if (p.Id != Environment.ProcessId) try { p.Kill(); } catch { }
                Installer.Uninstall();
                MessageBox.Show(T("TS6 Overlay został odinstalowany."), "TS6 Overlay");
            }
            Shutdown();
            return;
        }

        // Po aktualizacji/instalacji/restarcie poprzednia kopia jeszcze się zamyka — poczekaj na nią.
        bool handover = args.Contains("--updated") || args.Contains("--installed") || args.Contains("--restart");
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
        Cfg = Config.Load();   // ponownie: poprzednia kopia mogła właśnie zapisać zmiany
        L.Set(Cfg.Language);
        Theme.LoadCustom();
        Sounds.Volume = Cfg.Volume;
        Sounds.Files = new(Cfg.SoundFiles);

        if (args.Contains("--updated")) Installer.RefreshVersion();
        else if (!handover && OfferInstall()) { Shutdown(); return; }

        _win = new OverlayWindow(Cfg);
        _win.Moved += OnOverlayMoved;
        _tsRunning = IsTeamSpeakRunning();

        Ts = new TsClient(Cfg);
        Ts.StateChanged += () => Dispatcher.BeginInvoke(Refresh);
        Ts.Notice += n => Dispatcher.BeginInvoke(() => OnNotice(n));
        Ts.TalkChanged += (c, t) => Dispatcher.BeginInvoke(() => Stats.OnTalk(c, t));
        Ts.Status += s => Dispatcher.BeginInvoke(() => SetStatus(s));
        _ = Task.Run(() => Ts.RunAsync(_stop.Token));

        Mic.VoiceChanged += v => Dispatcher.BeginInvoke(() => OnVoiceWhileMuted(v));
        Voice = new Speech(Cfg);
        Discord = new DiscordPresence(Cfg);
        Lights = new Rgb(Cfg);
        if (Cfg.PhoneKey == "") { Cfg.PhoneKey = NewPhoneKey(); Cfg.Save(); }
        Phone = new PhoneServer(() => _phoneJson, (cmd, arg) => Dispatcher.Invoke(() => OnPhoneCommand(cmd, arg)));

        Binds = new BindManager(Cfg);
        Binds.PeekPressed += StartPeek;
        Binds.StoppedAll += () => Voice.Stop();
        _peekTimer.Tick += (_, _) => CheckPeekRelease();
        BuildTray();
        RegisterToggleHotkey();
        ListenForSecondInstance();
        ApplySettings();

        // Co sekundę: czy działa TeamSpeak, jaka gra jest na pierwszym planie (tryb „tylko w grach”, profile).
        var watch = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        watch.Tick += (_, _) => { WatchTick(); if (Phone.Running) _phoneJson = PhoneJson(); };
        watch.Start();

        // Co minutę: czas obecności do statystyk długoterminowych; co 5 minut zapis na dysk.
        int minutes = 0;
        var minute = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        minute.Tick += (_, _) =>
        {
            if (Cfg.KeepLongTermStats && _state.Servers.Count > 0)
                LongTerm.AddOnlineMinute(Ts.AllClients().Where(c => !Cfg.IsIgnored(c.Uid)), true);
            if (++minutes % 5 == 0) LongTerm.Save();
        };
        minute.Start();

        if (args.Contains("--updated"))
            _tray.ShowBalloonTip(4000, "TS6 Overlay", T("Zaktualizowano do wersji {0}.", Updater.Current), Forms.ToolTipIcon.Info);
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

    // ---------- instalacja i restart ----------

    bool OfferInstall()
    {
        if (Installer.IsInstalledCopy || Cfg.InstallPromptShown) return false;
        Cfg.InstallPromptShown = true;
        Cfg.Save();
        if (Installer.IsInstalled) return false;
        var r = MessageBox.Show(
            T("Zainstalować TS6 Overlay na tym komputerze?\n\n" +
              "• program trafi do %LOCALAPPDATA%\\Programs\\TS6Overlay,\n" +
              "• pojawi się skrót w menu Start,\n" +
              "• nakładka będzie startować z Windows (i pokazywać się razem z TeamSpeakiem),\n" +
              "• odinstalujesz go w Ustawieniach Windows → Aplikacje.\n\n" +
              "„Nie” = uruchom bez instalacji (możesz zainstalować później w Ustawieniach → Aktualizacje)."),
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
            MessageBox.Show(T("Instalacja nie powiodła się: {0}", ex.Message), "TS6 Overlay");
            return false;
        }
    }

    public void InstallNow()
    {
        try
        {
            Quit(beforeShutdown: () => { _single?.ReleaseMutex(); Installer.Install(); });
        }
        catch (Exception ex) { MessageBox.Show(T("Instalacja nie powiodła się: {0}", ex.Message), "TS6 Overlay"); }
    }

    /// <summary>Uruchamia program od nowa (np. po zmianie języka albo wczytaniu kopii ustawień).</summary>
    public void Restart(bool saveConfig = true)
    {
        Quit(saveConfig: saveConfig, beforeShutdown: () =>
        {
            _single?.ReleaseMutex();
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--restart") { UseShellExecute = true });
        });
    }

    // ---------- kopia ustawień ----------

    public void ExportBackup(string path) => Backup.Export(Cfg, path);

    /// <summary>Wczytuje kopię i restartuje program, żeby wszystko (bindy, motywy, dźwięki) wczytało się od nowa.</summary>
    public void ImportBackup(string path)
    {
        Binds.UnregisterAll();
        Backup.Import(Cfg, path);
        Restart(saveConfig: false);
    }

    // ---------- stan i zdarzenia ----------

    void Refresh()
    {
        _state = Ts.Snapshot();
        _win.View.Render(_state);
        Obs.Update(_state, _win.View.Theme, Cfg);
        Discord.Update(_state);
        if (Phone.Running) _phoneJson = PhoneJson();
        UpdateMic();
    }

    // ---------- panel na telefonie ----------

    static string NewPhoneKey() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();

    public void RegeneratePhoneKey()
    {
        Cfg.PhoneKey = NewPhoneKey();
        ApplySettings();
    }

    /// <summary>Adres panelu do wpisania w telefonie / zakodowania w QR.</summary>
    public string PhoneUrl(string? ip = null) =>
        $"http://{ip ?? PhoneServer.LocalAddresses().FirstOrDefault() ?? "localhost"}:{Cfg.PhonePort}/?k={Cfg.PhoneKey}";

    string PhoneJson()
    {
        object Member(ClientInfo m, int myId) => new
        {
            nick = m.Nickname, talking = m.Talking, whisper = m.Whisper, me = m.Id == myId,
            muted = m.InputMuted, deaf = m.OutputMuted, fav = Cfg.FavoriteFor(m.Uid) != null,
        };
        var (_, tree) = Ts.ServerTree();
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            servers = _state.Servers.Select(v => new
            {
                server = v.Server, channel = v.Channel,
                members = v.Members.Select(m => Member(m, v.MyId)),
                watched = v.Watched.Select(w => new { name = w.Name, members = w.Members.Select(m => Member(m, 0)) }),
            }),
            binds = Cfg.Binds.Where(b => b.Enabled && b.SoundPath != "").Select(b => new { id = b.Id, name = b.Name, key = b.Key != 0 ? b.KeyText : "" }),
            playing = Binds.PlayingId,
            tree = tree.Select(c => new { name = c.Name, mine = c.Mine, members = c.Members.Select(m => Member(m, _state.MyId)) }),
            messages = Stats.History.Where(h => h.Kind is NoticeKind.Message or NoticeKind.Poke).TakeLast(15).Reverse()
                .Select(h => new { time = h.Time.ToString("HH:mm"), text = h.Text }),
        });
    }

    void OnPhoneCommand(string cmd, string arg)
    {
        switch (cmd)
        {
            case "bind":
                var b = Cfg.Binds.FirstOrDefault(x => x.Id == arg && x.Enabled);
                if (b != null) Binds.Trigger(b);
                break;
            case "stop": Binds.StopAll(); break;
            case "toggle": ToggleVisible(); break;
        }
        _phoneJson = PhoneJson();
    }

    void OnNotice(Notice n)
    {
        _win.View.AddNotice(n);
        Obs.AddNotice(n);
        Obs.Update(_state, _win.View.Theme, Cfg);
        Stats.OnNotice(n);
        if (Cfg.KeepChatArchive) ChatArchive.Append(n, _state.Active?.Server ?? "");
        if (Phone.Running) _phoneJson = PhoneJson();

        var fav = n.Who != null ? Cfg.FavoriteFor(n.Who.Uid) : null;
        switch (n.Kind)
        {
            case NoticeKind.Join when Cfg.SoundOnJoin:
                if (fav != null) Sounds.Play(SoundKind.Favorite, fav.SoundPath);
                else Sounds.Play(SoundKind.Join);
                break;
            case NoticeKind.Friend when Cfg.SoundOnJoin && fav != null: Sounds.Play(SoundKind.Favorite, fav.SoundPath); break;
            case NoticeKind.Leave when Cfg.SoundOnLeave: Sounds.Play(SoundKind.Leave); break;
            case NoticeKind.Message when Cfg.SoundOnMessage: Sounds.Play(SoundKind.Message); break;
            case NoticeKind.Poke when Cfg.SoundOnPoke: Sounds.Play(SoundKind.Poke); break;
        }

        var flash = Cfg.RgbColorFor(n.Kind.ToString());
        if (flash != "") Lights.Flash(flash);

        if (Voice.Wants(n) && (!Cfg.TtsOnlyInGame || GameDetector.IsGameInForeground(Cfg) == true))
            Voice.Say(n.Speech ?? n.Text);
    }

    // ---------- podgląd pod klawiszem ----------

    void StartPeek(SoundBind b)
    {
        _peekBind = b;
        if (b == Cfg.PeekServerBind)
        {
            var (server, channels) = Ts.ServerTree();
            _win.View.ShowPeekServer(server, channels);
        }
        else
        {
            var msgs = Stats.History.Where(h => h.Kind is NoticeKind.Message or NoticeKind.Poke)
                .TakeLast(10).Select(h => (h.Time, h.Text, h.Kind));
            _win.View.ShowPeekMessages(msgs);
        }
        _peekForce = true;
        UpdateVisibility();
        _peekTimer.Start();
    }

    /// <summary>RegisterHotKey zgłasza tylko naciśnięcie — puszczenie klawisza sprawdzamy sami.</summary>
    void CheckPeekRelease()
    {
        if (_peekBind != null && (GetAsyncKeyState(_peekBind.Key) & 0x8000) != 0) return;
        _peekTimer.Stop();
        _peekBind = null;
        _peekForce = false;
        _win.View.EndPeek();
        UpdateVisibility();
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
        if (real && Cfg.RgbColorFor("MuteWarning") is { Length: > 0 } c && (DateTime.Now - _lastMuteFlash).TotalSeconds > 4)
        {
            _lastMuteFlash = DateTime.Now;
            Lights.Flash(c);
        }
    }

    // ---------- widoczność: TeamSpeak, gry, profile, skrót ----------

    void WatchTick()
    {
        bool changed = false;
        bool running = IsTeamSpeakRunning();
        if (running != _tsRunning) { _tsRunning = running; changed = true; }

        bool? game = Cfg.GameOnly || Cfg.Profiles.Count > 0 ? GameDetector.IsGameInForeground(Cfg) : false;
        if (Cfg.GameOnly && game.HasValue && game.Value != _gameActive) { _gameActive = game.Value; changed = true; }

        if (Cfg.Profiles.Count > 0 && game.HasValue)
        {
            // null = okno neutralne (TS, nakładka) — profil zostaje bez zmian
            var fg = GameDetector.ForegroundProcess();
            var profile = Cfg.Profiles.FirstOrDefault(p => GameDetector.Normalize(p.Game).Equals(fg, StringComparison.OrdinalIgnoreCase));
            if (profile != ActiveProfile) ApplyProfile(profile);
        }
        else if (Cfg.Profiles.Count == 0 && ActiveProfile != null) ApplyProfile(null);

        if (changed) UpdateVisibility();
    }

    void ApplyProfile(GameProfile? p)
    {
        ActiveProfile = p;
        _win.Left = p?.Left ?? Cfg.Left;
        _win.Top = p?.Top ?? Cfg.Top;
        _win.ApplyScale(p?.Scale ?? Cfg.Scale, p?.Opacity ?? Cfg.Opacity);
        _win.View.ApplyTheme(_previewTheme ?? EffectiveTheme());
        Obs.Update(_state, _win.View.Theme, Cfg);
    }

    Theme EffectiveTheme() => Theme.ByName(ActiveProfile is { Theme: { Length: > 0 } t } ? t : Cfg.Theme);

    void OnOverlayMoved(double left, double top)
    {
        if (ActiveProfile != null) { ActiveProfile.Left = left; ActiveProfile.Top = top; }
        else { Cfg.Left = left; Cfg.Top = top; }
        Cfg.Save();
    }

    static bool IsTeamSpeakRunning()
    {
        var p = Process.GetProcessesByName("TeamSpeak");
        foreach (var x in p) x.Dispose();
        return p.Length > 0;
    }

    void UpdateVisibility()
    {
        bool show = _peekForce || (_tsRunning && !_userHidden && (!Cfg.GameOnly || _gameActive || _win.EditMode));
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
        // Profil mógł zostać zmieniony lub usunięty w ustawieniach.
        if (ActiveProfile != null && !Cfg.Profiles.Contains(ActiveProfile)) ActiveProfile = null;
        _win.ApplyScale(ActiveProfile?.Scale ?? Cfg.Scale, ActiveProfile?.Opacity ?? Cfg.Opacity);
        _win.View.ApplyTheme(_previewTheme ?? EffectiveTheme());
        _win.View.ResetIdle();
        if (Cfg.ObsEnabled && (Obs.Port != Cfg.ObsPort || !ObsRunning)) ObsRunning = Obs.Start(Cfg.ObsPort);
        else if (!Cfg.ObsEnabled && ObsRunning) { Obs.Stop(); ObsRunning = false; }
        if (Cfg.PhoneEnabled && (!Phone.Running || Phone.Port != Cfg.PhonePort || Phone.Key != Cfg.PhoneKey)) Phone.Start(Cfg.PhonePort, Cfg.PhoneKey);
        else if (!Cfg.PhoneEnabled && Phone.Running) Phone.Stop();
        Stats.LongTerm = Cfg.KeepLongTermStats ? LongTerm : null;
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
        _win.View.ApplyTheme(t ?? EffectiveTheme());
        Obs.Update(_state, _win.View.Theme, Cfg);
    }

    /// <summary>Przenosi nakładkę w zapisane miejsce (ustawienia główne albo aktywny profil).</summary>
    public void ResetOverlayPosition()
    {
        _win.Left = ActiveProfile?.Left ?? Cfg.Left;
        _win.Top = ActiveProfile?.Top ?? Cfg.Top;
    }

    /// <summary>Bieżąca pozycja i rozmiar nakładki — do zapisania w profilu gry.</summary>
    public (double left, double top) OverlayPosition => (_win.Left, _win.Top);

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
            _statsWin = new StatsWindow(Stats, LongTerm);
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
                if (interactive) MessageBox.Show(T("Masz najnowszą wersję ({0}).", Updater.Current), "TS6 Overlay");
                return;
            }
            if (!interactive && r.Tag == Cfg.SkippedVersion) return;
            if (interactive) await AskAndUpdate(r);
            else _tray.ShowBalloonTip(8000, T("TS6 Overlay — aktualizacja"),
                T("Dostępna wersja {0}. Kliknij, aby zaktualizować.", r.Version), Forms.ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            if (interactive) MessageBox.Show(T("Nie udało się sprawdzić aktualizacji: {0}", ex.Message), "TS6 Overlay");
        }
    }

    public async Task AskAndUpdate(Updater.Release r)
    {
        var notes = r.Notes.Length > 600 ? r.Notes[..600] + "…" : r.Notes;
        var ans = MessageBox.Show(T("Dostępna wersja {0} (masz {1}).\n\n{2}\n\nZaktualizować teraz?", r.Version, Updater.Current, notes),
            T("TS6 Overlay — aktualizacja"), MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (ans == MessageBoxResult.No) { Cfg.SkippedVersion = r.Tag; Cfg.Save(); return; }
        if (ans != MessageBoxResult.Yes) return;
        try
        {
            SetStatus(T("Pobieranie aktualizacji…"));
            await Updater.InstallAsync(r);
            Quit(beforeShutdown: () => _single?.ReleaseMutex());
        }
        catch (Exception ex)
        {
            MessageBox.Show(T("Aktualizacja nie powiodła się: {0}", ex.Message), "TS6 Overlay");
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

        var settings = Item(T("Ustawienia…"), () => OpenSettings());
        settings.Font = new System.Drawing.Font(settings.Font, System.Drawing.FontStyle.Bold);
        menu.Items.Add(settings);
        menu.Items.Add(Item(T("Statystyki sesji…"), OpenStats));
        menu.Items.Add(new Forms.ToolStripSeparator());

        menu.Items.Add(Item((_userHidden ? T("Pokaż nakładkę") : T("Ukryj nakładkę")) + "  (Ctrl+Shift+O)", ToggleVisible));
        menu.Items.Add(Item(_win.EditMode ? T("Zablokuj pozycję") : T("Przesuń nakładkę"), () =>
        {
            _win.EditMode = !_win.EditMode;
            UpdateVisibility();
        }));

        var theme = new Forms.ToolStripMenuItem(T("Motyw"));
        foreach (var t in Theme.All)
            theme.DropDownItems.Add(Item(t.Name, () =>
            {
                Cfg.Theme = t.Name;
                ApplySettings();
                _win.View.AddNotice(new(T("Motyw: {0}", t.Name), NoticeKind.Info));
            }, Theme.ByName(Cfg.Theme).Name == t.Name));
        menu.Items.Add(theme);

        menu.Items.Add(Item(T("Pokazuj wszystkich na kanale"), () => { Cfg.ShowChannelList = !Cfg.ShowChannelList; ApplySettings(); }, Cfg.ShowChannelList));
        menu.Items.Add(Item(T("Autoukrywanie, gdy nikt nie mówi"), () => { Cfg.AutoHide = !Cfg.AutoHide; ApplySettings(); }, Cfg.AutoHide));
        menu.Items.Add(Item(T("Pokazuj tylko w grach"), () =>
        {
            Cfg.GameOnly = !Cfg.GameOnly;
            if (Cfg.GameOnly && Cfg.Games.Count == 0 && !Cfg.GameFullscreenAny)
                OpenSettings(SettingsWindow.TabBehavior);
            ApplySettings();
        }, Cfg.GameOnly));
        menu.Items.Add(Item(T("Lektor (czytanie na głos)"), () => { Cfg.TtsEnabled = !Cfg.TtsEnabled; ApplySettings(); if (!Cfg.TtsEnabled) Voice.Stop(); }, Cfg.TtsEnabled));

        var sound = new Forms.ToolStripMenuItem(T("Dźwięki"));
        sound.DropDownItems.Add(Item(T("Gdy ktoś wchodzi na kanał"), () => { Cfg.SoundOnJoin = !Cfg.SoundOnJoin; ApplySettings(); if (Cfg.SoundOnJoin) Sounds.Play(SoundKind.Join); }, Cfg.SoundOnJoin));
        sound.DropDownItems.Add(Item(T("Gdy ktoś wychodzi z kanału"), () => { Cfg.SoundOnLeave = !Cfg.SoundOnLeave; ApplySettings(); if (Cfg.SoundOnLeave) Sounds.Play(SoundKind.Leave); }, Cfg.SoundOnLeave));
        sound.DropDownItems.Add(Item(T("Wiadomości"), () => { Cfg.SoundOnMessage = !Cfg.SoundOnMessage; ApplySettings(); if (Cfg.SoundOnMessage) Sounds.Play(SoundKind.Message); }, Cfg.SoundOnMessage));
        sound.DropDownItems.Add(Item(T("Szturchnięcia"), () => { Cfg.SoundOnPoke = !Cfg.SoundOnPoke; ApplySettings(); if (Cfg.SoundOnPoke) Sounds.Play(SoundKind.Poke); }, Cfg.SoundOnPoke));
        menu.Items.Add(sound);

        var binds = new Forms.ToolStripMenuItem(T("Bindy (soundboard)"));
        foreach (var b in Cfg.Binds.Where(x => x.Enabled && x.SoundPath != ""))
            binds.DropDownItems.Add(Item($"{b.Name}   [{b.KeyText}]", () => Binds.Trigger(b)));
        if (binds.DropDownItems.Count > 0) binds.DropDownItems.Add(new Forms.ToolStripSeparator());
        binds.DropDownItems.Add(Item(T("Zatrzymaj wszystkie dźwięki") + (Cfg.StopBind.Key != 0 ? $"   [{Cfg.StopBind.KeyText}]" : ""), () => Binds.StopAll()));
        binds.DropDownItems.Add(Item(T("Ustaw bindy…"), () => OpenSettings(SettingsWindow.TabBinds)));
        menu.Items.Add(binds);

        menu.Items.Add(Item(T("Nakładka dla OBS") + (ObsRunning ? $"  ({Obs.Url})" : ""), () => OpenSettings(SettingsWindow.TabObs), Cfg.ObsEnabled));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Item(PendingUpdate != null ? T("Zaktualizuj do {0}…", PendingUpdate.Version) : T("Sprawdź aktualizacje"), () => CheckForUpdates(true)));
        menu.Items.Add(Item(T("Zamknij"), () => Quit()));
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

    void Quit(Action? beforeShutdown = null, bool saveConfig = true)
    {
        _stop.Cancel();
        Mic.Dispose();
        Obs.Dispose();
        Binds?.Dispose();
        Voice?.Dispose();
        Discord?.Dispose();
        Phone?.Dispose();
        LongTerm.Save();
        if (_hotkeySrc != null) { UnregisterHotKey(_hotkeySrc.Handle, HOTKEY_ID); _hotkeySrc.Dispose(); }
        _tray.Visible = false;
        _tray.Dispose();
        if (_win.EditMode) _win.EditMode = false;
        if (saveConfig) Cfg.Save();
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
            w.View.Render(OverlayView.DemoState(cfg, muted: true));
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

        // Przykłady nowych widoków: dwa serwery + obserwowany kanał, podgląd serwera, podgląd wiadomości.
        var c2 = new Config { Theme = "Terminal", Opacity = 1, WatchedChannels = { "Lobby" } };
        var demo = OverlayView.DemoState(c2);
        var second = new ServerView(2, "drugi-serwer.pl", "Rozmowy", new()
        {
            new() { Id = 11, Nickname = "Zosia", Talking = true },
            new() { Id = 12, Nickname = "Bartek" },
        }, 0, false, false, new(), false);
        var shots = new (string name, Action<OverlayView> setup)[]
        {
            ("multi", v => { v.Render(new OverlayState(new List<ServerView> { demo.Servers[0], second })); v.ClearNotices();
                             v.AddNotice(new(T("★ {0} jest online ({1})", "Ola", "Lobby"), NoticeKind.Friend), true); }),
            ("peek-server", v => { v.Render(demo); v.ShowPeekServer("yen.com.pl", new()
                {
                    new("Lobby", new() { new() { Id = 7, Nickname = "Piotr" }, new() { Id = 8, Nickname = "Ewa" } }),
                    new("Pluton 9", demo.Servers[0].Members, true),
                    new("AFK", new() { new() { Id = 9, Nickname = "Tomek", OutputMuted = true } }),
                }); }),
            ("peek-messages", v => { v.Render(demo); v.ShowPeekMessages(new[]
                {
                    (DateTime.Now.AddMinutes(-5), "Kamil: " + T("idziemy na B?"), NoticeKind.Message),
                    (DateTime.Now.AddMinutes(-2), T("{0} szturcha Cię!", "Marek"), NoticeKind.Poke),
                    (DateTime.Now, "Ola: gg", NoticeKind.Message),
                }); }),
        };
        foreach (var (name, setup) in shots)
        {
            var w = new OverlayWindow(c2) { Left = -5000 };
            w.Show();
            setup(w.View);
            w.UpdateLayout();
            var el = (FrameworkElement)w.Content;
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)Math.Ceiling(el.ActualWidth), (int)Math.Ceiling(el.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bmp.Render(el);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var f = System.IO.File.Create(System.IO.Path.Combine(dir, name + ".png"));
            enc.Save(f);
            w.Close();
        }
    }
}
