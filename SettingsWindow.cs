using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using static TS6Overlay.L;
using Forms = System.Windows.Forms;

namespace TS6Overlay;

/// <summary>Okno ustawień: wszystkie opcje z podglądem nakładki na żywo. Zmiany zapisują się od razu.</summary>
public sealed class SettingsWindow : Window
{
    public const int TabLook = 0, TabEditor = 1, TabBehavior = 2, TabNotify = 3, TabBinds = 4, TabPeople = 5, TabObs = 6, TabPhone = 7, TabUpdates = 8;

    readonly App _app;
    readonly Config _cfg;
    readonly TabControl _tabs = new() { TabStripPlacement = Dock.Left };
    readonly OverlayView _preview;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    // kolory okna (ciemne, niezależnie od motywu nakładki)
    static readonly Brush Bg = Theme.B("#FF1B1E24"), Card = Theme.B("#FF242830"), Fg = Theme.B("#FFE6E8EB"),
        Dim = Theme.B("#FF9AA1AC"), AccentB = Theme.B("#FF2FBF55"), InputBg = Theme.B("#FF2E333C"), Line = Theme.B("#FF3A404B");

    public SettingsWindow(App app)
    {
        _app = app;
        _cfg = app.Cfg;
        Title = T("TS6 Overlay {0} — ustawienia", Updater.Current);
        Width = 1180; Height = 760; MinWidth = 860; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Bg;
        Foreground = Fg;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        try { Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
            System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)!.Handle, Int32Rect.Empty,
            System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions()); } catch { }
        Resources.MergedDictionaries.Add(Styles());

        _preview = new OverlayView(_cfg, preview: true);
        _preview.ApplyTheme(Theme.ByName(_cfg.Theme));
        _preview.ShowDemo();

        _tabs.Items.Add(Tab("Wygląd", LookTab()));
        _tabs.Items.Add(Tab("Edytor motywów", EditorTab()));
        _tabs.Items.Add(Tab("Zachowanie", BehaviorTab()));
        _tabs.Items.Add(Tab("Powiadomienia", NotifyTab()));
        _tabs.Items.Add(Tab("Bindy", BindsTab()));
        _tabs.Items.Add(Tab("Osoby", PeopleTab()));
        _tabs.Items.Add(Tab("OBS", ObsTab()));
        _tabs.Items.Add(Tab("Integracje", PhoneTab()));
        _tabs.Items.Add(Tab("Aktualizacje i kopia", UpdatesTab()));
        _tabs.SelectionChanged += (_, e) =>
        {
            if (e.Source != _tabs) return;
            if (_tabs.SelectedIndex != TabEditor) { _app.PreviewTheme(null); ShowTheme(Theme.ByName(_cfg.Theme)); }
            else ShowTheme(_edit);
            if (_tabs.SelectedIndex == TabPeople) RebuildPeople();
            if (_tabs.SelectedIndex == TabBehavior) RebuildProfiles();
            if (_tabs.SelectedIndex != TabNotify) _app.MicTest = false;
            if (_tabs.SelectedIndex == TabBinds) RebuildBinds();
            else CancelCapture();
        };

        var previewBox = new Border
        {
            Background = Checker(),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Child = new Viewbox { Child = _preview, StretchDirection = StretchDirection.DownOnly, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left },
        };
        var right = new DockPanel { Margin = new Thickness(12, 0, 0, 0) };
        var cap = Label("Podgląd (przykładowe dane)", dim: true);
        DockPanel.SetDock(cap, Dock.Top);
        right.Children.Add(cap);
        right.Children.Add(previewBox);

        var grid = new Grid { Margin = new Thickness(12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });
        grid.Children.Add(_tabs);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        Content = grid;

        _timer.Tick += (_, _) => TimerTick();
        _timer.Start();
        Closed += (_, _) => { _timer.Stop(); CancelCapture(); };
        PreviewKeyDown += OnCaptureKey;
    }

    public void SelectTab(int i) => _tabs.SelectedIndex = i;

    void Changed()
    {
        _app.ApplySettings();
        ShowTheme(_tabs.SelectedIndex == TabEditor ? _edit : Theme.ByName(_cfg.Theme));
    }

    void ShowTheme(Theme t)
    {
        _preview.ApplyTheme(t);
        _preview.ShowDemo();
    }

    // =====================================================================
    // Wygląd
    // =====================================================================

    ComboBox _themeCombo = null!;

    UIElement LookTab()
    {
        var p = Page();
        p.Children.Add(Header("Motyw"));
        _themeCombo = new ComboBox { MinWidth = 240 };
        FillThemeCombo();
        _themeCombo.SelectionChanged += (_, _) =>
        {
            if (_themeCombo.SelectedItem is string n && n != _cfg.Theme) { _cfg.Theme = n; Changed(); }
        };
        p.Children.Add(Row(_themeCombo,
            Btn("Edytuj kopię…", () => { LoadEditor(Theme.ByName(_cfg.Theme), copy: true); SelectTab(TabEditor); }),
            Btn("Importuj…", ImportTheme),
            Btn("Eksportuj…", () => ExportTheme(Theme.ByName(_cfg.Theme))),
            Btn("Usuń", () =>
            {
                var t = Theme.ByName(_cfg.Theme);
                if (t.IsBuiltin) { Info("Wbudowanych motywów nie można usunąć."); return; }
                if (!Confirm(T("Usunąć motyw „{0}”?", t.Name))) return;
                Theme.DeleteCustom(t.Name);
                _cfg.Theme = "Terminal";
                FillThemeCombo();
                Changed();
            })));
        p.Children.Add(Hint("Motywy od znajomych to pliki *.ts6theme — wczytasz je przyciskiem „Importuj…”."));

        p.Children.Add(Header("Język"));
        var lang = new ComboBox { MinWidth = 240 };
        lang.Items.Add(T("Automatycznie (jak Windows)"));
        lang.Items.Add("Polski");
        lang.Items.Add("English");
        lang.SelectedIndex = _cfg.Language switch { "pl" => 1, "en" => 2, _ => 0 };
        lang.SelectionChanged += (_, _) =>
        {
            var v = lang.SelectedIndex switch { 1 => "pl", 2 => "en", _ => "" };
            if (v == _cfg.Language) return;
            _cfg.Language = v;
            _cfg.Save();
            if (Confirm(T("Język zmieni się po ponownym uruchomieniu programu. Uruchomić ponownie teraz?"))) _app.Restart();
        };
        p.Children.Add(Row(lang));

        p.Children.Add(Header("Rozmiar i przezroczystość"));
        p.Children.Add(SliderRow("Rozmiar", 60, 200, () => _cfg.Scale * 100, v => _cfg.Scale = v / 100, v => $"{v:0}%"));
        p.Children.Add(SliderRow("Przezroczystość", 30, 100, () => _cfg.Opacity * 100, v => _cfg.Opacity = v / 100, v => $"{v:0}%"));

        p.Children.Add(Header("Zawartość"));
        p.Children.Add(Check("Pokazuj wszystkich na kanale (odznacz = tylko mówiący i powiadomienia)", () => _cfg.ShowChannelList, v => _cfg.ShowChannelList = v));
        p.Children.Add(Check("Pokazuj wszystkie serwery, z którymi połączony jest TeamSpeak (nie tylko aktywny)", () => _cfg.ShowAllServers, v => _cfg.ShowAllServers = v));
        p.Children.Add(SliderRow("Czas powiadomień wejścia/wyjścia", 2, 30, () => _cfg.EventSeconds, v => _cfg.EventSeconds = (int)v, v => $"{v:0} s"));
        p.Children.Add(SliderRow("Czas wiadomości i szturchnięć", 3, 60, () => _cfg.MessageSeconds, v => _cfg.MessageSeconds = (int)v, v => $"{v:0} s"));

        p.Children.Add(Header("Położenie"));
        p.Children.Add(Hint("Przytrzymaj Ctrl i przeciągnij nakładkę lewym przyciskiem myszy. Ctrl+Shift+O ukrywa/pokazuje nakładkę."));
        p.Children.Add(Row(Btn("Przywróć pozycję (lewy górny róg)", () =>
        {
            if (_app.ActiveProfile is { } prof) { prof.Left = 20; prof.Top = 200; } else { _cfg.Left = 20; _cfg.Top = 200; }
            _app.ResetOverlayPosition();
            Changed();
        })));
        return Scroll(p);
    }


    void FillThemeCombo()
    {
        if (_themeCombo == null) return;
        _themeCombo.ItemsSource = Theme.All.Select(t => t.Name).ToList();
        _themeCombo.SelectedItem = Theme.ByName(_cfg.Theme).Name;
        if (_baseCombo != null) _baseCombo.ItemsSource = Theme.All.Select(t => t.Name).ToList();
    }

    void ImportTheme()
    {
        var d = new Microsoft.Win32.OpenFileDialog { Filter = T("Motyw TS6 Overlay") + " (*.ts6theme)|*.ts6theme|JSON (*.json)|*.json", Title = T("Importuj motyw") };
        if (d.ShowDialog(this) != true) return;
        try
        {
            var t = Theme.Import(d.FileName);
            _cfg.Theme = t.Name;
            FillThemeCombo();
            Changed();
            Info(T("Zaimportowano motyw „{0}”.", t.Name));
        }
        catch (Exception ex) { Info(T("Nie udało się wczytać motywu: {0}", ex.Message)); }
    }

    void ExportTheme(Theme t)
    {
        var d = new Microsoft.Win32.SaveFileDialog { Filter = T("Motyw TS6 Overlay") + " (*.ts6theme)|*.ts6theme", FileName = t.Name + Theme.Extension, Title = T("Eksportuj motyw") };
        if (d.ShowDialog(this) != true) return;
        try { t.Export(d.FileName); Info("Zapisano. Wyślij ten plik znajomemu — wczyta go przyciskiem „Importuj…”."); }
        catch (Exception ex) { Info(T("Nie udało się zapisać: {0}", ex.Message)); }
    }

    // =====================================================================
    // Edytor motywów
    // =====================================================================

    Theme _edit = Theme.ByName("Terminal");
    ComboBox? _baseCombo;
    TextBox _nameBox = null!;
    StackPanel _colorRows = null!;
    ComboBox _fontCombo = null!;
    Slider _radius = null!, _border = null!;
    CheckBox _square = null!, _shadow = null!, _glow = null!;
    bool _loadingEditor;

    UIElement EditorTab()
    {
        var p = Page();
        p.Children.Add(Header("Motyw bazowy"));
        _baseCombo = new ComboBox { MinWidth = 220, ItemsSource = Theme.All.Select(t => t.Name).ToList() };
        _baseCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingEditor || _baseCombo.SelectedItem is not string n) return;
            LoadEditor(Theme.ByName(n), copy: Theme.ByName(n).IsBuiltin);
        };
        p.Children.Add(Row(_baseCombo));
        p.Children.Add(Hint("Wybierz motyw, zmień kolory i zapisz pod własną nazwą. Zmiany widać od razu w podglądzie i na nakładce."));

        p.Children.Add(Header("Nazwa"));
        _nameBox = Input(240);
        p.Children.Add(Row(_nameBox));

        p.Children.Add(Header("Kolory"));
        p.Children.Add(Hint("Kwadrat = kolor (kliknij, aby wybrać), pole = kod #AARRGGBB, suwak = krycie."));
        _colorRows = new StackPanel();
        p.Children.Add(_colorRows);

        p.Children.Add(Header("Kształt i czcionka"));
        _fontCombo = new ComboBox
        {
            MinWidth = 220,
            ItemsSource = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(x => x).ToList(),
            IsEditable = true,
        };
        _fontCombo.SelectionChanged += (_, _) => { if (_fontCombo.SelectedItem is string f) EditChanged(_edit with { Font = f }); };
        p.Children.Add(Row(Label("Czcionka"), _fontCombo));
        _radius = new Slider { Minimum = 0, Maximum = 20, Width = 220, IsSnapToTickEnabled = true, TickFrequency = 1 };
        _radius.ValueChanged += (_, _) => EditChanged(_edit with { Radius = _radius.Value });
        p.Children.Add(Row(Label("Zaokrąglenie rogów", 160), _radius));
        _border = new Slider { Minimum = 0, Maximum = 4, Width = 220, IsSnapToTickEnabled = true, TickFrequency = 1 };
        _border.ValueChanged += (_, _) => EditChanged(_edit with { BorderWidth = _border.Value });
        p.Children.Add(Row(Label("Grubość ramki", 160), _border));
        _square = RawCheck("Kwadratowe znaczniki zamiast kropek", v => EditChanged(_edit with { SquareDots = v }));
        _shadow = RawCheck("Cień pod tekstem (lepsza czytelność bez tła)", v => EditChanged(_edit with { TextShadow = v }));
        _glow = RawCheck("Poświata znacznika osoby, która mówi", v => EditChanged(_edit with { Glow = v }));
        p.Children.Add(_square);
        p.Children.Add(_shadow);
        p.Children.Add(_glow);

        p.Children.Add(Header("Zapis"));
        p.Children.Add(Row(
            Btn("Zapisz i użyj", SaveEditedTheme, primary: true),
            Btn("Eksportuj do pliku…", () => ExportTheme(_edit with { Name = _nameBox.Text.Trim() is { Length: > 0 } n ? n : _edit.Name })),
            Btn("Cofnij zmiany", () => LoadEditor(Theme.ByName(_baseCombo.SelectedItem as string), copy: false))));

        LoadEditor(Theme.ByName(_cfg.Theme), copy: Theme.ByName(_cfg.Theme).IsBuiltin);
        return Scroll(p);
    }

    void LoadEditor(Theme t, bool copy)
    {
        _loadingEditor = true;
        _edit = t;
        if (_baseCombo != null) _baseCombo.SelectedItem = t.Name;
        _nameBox.Text = copy ? UniqueName(t.Name + T(" (mój)")) : t.Name;
        _fontCombo.SelectedItem = t.Font;
        _fontCombo.Text = t.Font;
        _radius.Value = t.Radius;
        _border.Value = t.BorderWidth;
        _square.IsChecked = t.SquareDots;
        _shadow.IsChecked = t.TextShadow;
        _glow.IsChecked = t.Glow;
        BuildColorRows();
        _loadingEditor = false;
        if (_tabs.SelectedIndex == TabEditor) { ShowTheme(_edit); _app.PreviewTheme(_edit); }
    }

    static string UniqueName(string n)
    {
        string name = n;
        for (int i = 2; Theme.All.Any(x => x.Name == name); i++) name = $"{n} {i}";
        return name;
    }

    void EditChanged(Theme t)
    {
        if (_loadingEditor) return;
        _edit = t;
        ShowTheme(t);
        _app.PreviewTheme(t);
    }

    void BuildColorRows()
    {
        _colorRows.Children.Clear();
        foreach (var (prop, label) in Theme.ColorFields)
        {
            string hex = _edit.GetColor(prop);
            var swatch = new Button { Width = 34, Height = 24, Margin = new Thickness(0, 0, 8, 0), BorderBrush = Line, Cursor = Cursors.Hand, Background = Theme.B(hex), Style = null };
            var box = Input(110);
            box.Text = hex;
            var alpha = new Slider { Minimum = 0, Maximum = 255, Width = 120, Value = Theme.C(hex).A, VerticalAlignment = VerticalAlignment.Center };
            bool sync = false;

            void Set(string newHex)
            {
                try { Theme.C(newHex); } catch { box.BorderBrush = Brushes.IndianRed; return; }
                box.BorderBrush = Line;
                sync = true;
                box.Text = newHex.ToUpperInvariant();
                swatch.Background = Theme.B(newHex);
                alpha.Value = Theme.C(newHex).A;
                sync = false;
                EditChanged(_edit.WithColor(prop, newHex.ToUpperInvariant()));
            }

            swatch.Click += (_, _) =>
            {
                var c = Theme.C(box.Text);
                using var dlg = new Forms.ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B) };
                if (dlg.ShowDialog() == Forms.DialogResult.OK)
                    Set($"#{c.A:X2}{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}");
            };
            box.LostFocus += (_, _) => { if (!sync) Set(box.Text.Trim()); };
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Set(box.Text.Trim()); };
            alpha.ValueChanged += (_, _) =>
            {
                if (sync) return;
                try
                {
                    var c = Theme.C(box.Text);
                    Set($"#{(byte)alpha.Value:X2}{c.R:X2}{c.G:X2}{c.B:X2}");
                }
                catch { }
            };
            var row = Row(Label(label, 190), swatch, box, Label("krycie", dim: true), alpha);
            row.Margin = new Thickness(0, 2, 0, 2);
            _colorRows.Children.Add(row);
        }
    }

    void SaveEditedTheme()
    {
        var name = _nameBox.Text.Trim();
        if (name == "") { Info("Podaj nazwę motywu."); return; }
        if (Theme.Builtin.Any(b => b.Name == name)) { Info("Ta nazwa należy do wbudowanego motywu — wybierz inną."); return; }
        var t = _edit with { Name = name };
        try { t.SaveCustom(); }
        catch (Exception ex) { Info(T("Nie udało się zapisać: {0}", ex.Message)); return; }
        _cfg.Theme = name;
        _app.PreviewTheme(null);
        FillThemeCombo();
        _loadingEditor = true;
        _baseCombo!.SelectedItem = name;
        _loadingEditor = false;
        _edit = t;
        Changed();
        Info(T("Zapisano motyw „{0}” i ustawiono go na nakładce.", name));
    }

    // =====================================================================
    // Zachowanie
    // =====================================================================

    ListBox _gamesList = null!;
    ComboBox _procCombo = null!;

    UIElement BehaviorTab()
    {
        var p = Page();
        p.Children.Add(Header("Uruchamianie"));
        p.Children.Add(Check("Uruchamiaj z Windows (nakładka pojawia się razem z TeamSpeakiem)",
            () => Installer.AutostartEnabled, v => { try { Installer.AutostartEnabled = v; } catch (Exception ex) { Info(ex.Message); } }));

        p.Children.Add(Header("Autoukrywanie"));
        p.Children.Add(Check("Ukrywaj, gdy nikt nie mówi", () => _cfg.AutoHide, v => _cfg.AutoHide = v));
        p.Children.Add(SliderRow("Po ilu sekundach ciszy", 2, 60, () => _cfg.AutoHideSeconds, v => _cfg.AutoHideSeconds = (int)v, v => $"{v:0} s"));
        var fade = new RadioButton { Content = T("Nakładka blednie"), IsChecked = _cfg.AutoHideMode != "Header", Foreground = Fg, Margin = new Thickness(0, 4, 16, 4) };
        var header = new RadioButton { Content = T("Zostaje sam nagłówek z nazwą kanału"), IsChecked = _cfg.AutoHideMode == "Header", Foreground = Fg, Margin = new Thickness(0, 4, 0, 4) };
        fade.Checked += (_, _) => { _cfg.AutoHideMode = "Fade"; Changed(); };
        header.Checked += (_, _) => { _cfg.AutoHideMode = "Header"; Changed(); };
        p.Children.Add(Row(fade, header));
        p.Children.Add(SliderRow("Widoczność po zblednięciu", 0, 60, () => _cfg.AutoHideOpacity * 100, v => _cfg.AutoHideOpacity = v / 100, v => $"{v:0}%"));
        p.Children.Add(Hint("Nakładka wraca natychmiast, gdy ktoś zacznie mówić, wejdzie na kanał albo przyjdzie wiadomość."));

        p.Children.Add(Header("Pokazuj tylko w grach"));
        p.Children.Add(Check("Pokazuj nakładkę tylko, gdy gra jest na pierwszym planie", () => _cfg.GameOnly, v => _cfg.GameOnly = v));
        p.Children.Add(Check("Każda aplikacja na pełnym ekranie to gra (bez przeglądarek i odtwarzaczy wideo)", () => _cfg.GameFullscreenAny, v => _cfg.GameFullscreenAny = v));
        p.Children.Add(Label("Lista gier (nazwa pliku .exe):"));
        _gamesList = new ListBox { Height = 120, Background = InputBg, Foreground = Fg, BorderBrush = Line, Margin = new Thickness(0, 4, 0, 4) };
        RefreshGames();
        p.Children.Add(_gamesList);
        _procCombo = new ComboBox { MinWidth = 220, IsEditable = true };
        RefreshProcesses();
        p.Children.Add(Row(_procCombo,
            Btn("Dodaj", () =>
            {
                var name = GameDetector.Normalize(_procCombo.Text ?? "");
                if (name == "" || _cfg.Games.Any(g => g.Equals(name, StringComparison.OrdinalIgnoreCase))) return;
                _cfg.Games.Add(name);
                RefreshGames();
                Changed();
            }),
            Btn("Odśwież listę programów", RefreshProcesses),
            Btn("Usuń zaznaczoną", () =>
            {
                if (_gamesList.SelectedItem is string g) { _cfg.Games.Remove(g); RefreshGames(); Changed(); }
            })));
        p.Children.Add(Hint("Uruchom grę, kliknij „Odśwież listę programów”, wybierz ją z listy i „Dodaj”. Możesz też wpisać nazwę ręcznie, np. cs2."));

        p.Children.Add(Header("Profile gier"));
        p.Children.Add(Hint("Osobna pozycja, rozmiar, przezroczystość i motyw nakładki dla wybranej gry. Program przełącza profil sam, gdy gra jest na pierwszym planie. " +
                            "Gdy profil jest aktywny, przesunięcie nakładki (Ctrl + mysz) zapisuje pozycję w profilu."));
        _profileList = new StackPanel();
        p.Children.Add(_profileList);
        var profProc = new ComboBox { MinWidth = 200, IsEditable = true, ItemsSource = GameDetector.WindowedProcesses() };
        p.Children.Add(Row(profProc,
            Btn("+ Dodaj profil", () =>
            {
                var g = GameDetector.Normalize(profProc.Text ?? "");
                if (g == "") { Info(T("Wybierz grę z listy albo wpisz nazwę pliku .exe.")); return; }
                if (_cfg.Profiles.Any(x => x.Game.Equals(g, StringComparison.OrdinalIgnoreCase))) { Info(T("Ta gra ma już profil.")); return; }
                var (l, t) = _app.OverlayPosition;
                _cfg.Profiles.Add(new GameProfile { Game = g, Left = l, Top = t, Scale = _cfg.Scale, Opacity = _cfg.Opacity });
                Changed();
                RebuildProfiles();
            }),
            Btn("Odśwież listę programów", () => profProc.ItemsSource = GameDetector.WindowedProcesses())));
        RebuildProfiles();

        p.Children.Add(Header("Historia"));
        p.Children.Add(Check("Zapisuj statystyki długoterminowe (kto ile był na serwerze i mówił)", () => _cfg.KeepLongTermStats, v => _cfg.KeepLongTermStats = v));
        p.Children.Add(Check("Zapisuj archiwum czatu (wiadomości i szturchnięcia)", () => _cfg.KeepChatArchive, v => _cfg.KeepChatArchive = v));
        p.Children.Add(Hint("Wykresy i wyszukiwarkę znajdziesz w oknie statystyk (ikona w zasobniku → Statystyki…). Dane zostają tylko na tym komputerze."));

        p.Children.Add(Header("Diagnostyka"));
        p.Children.Add(Check("Zapisuj surowe zdarzenia TeamSpeak (events.log)", () => _cfg.LogRawEvents, v => _cfg.LogRawEvents = v));
        p.Children.Add(Row(Btn("Otwórz folder ustawień", () => Process.Start(new ProcessStartInfo("explorer.exe", Config.Dir)))));
        return Scroll(p);
    }

    StackPanel? _profileList;

    void RebuildProfiles()
    {
        if (_profileList == null) return;
        _profileList.Children.Clear();
        if (_cfg.Profiles.Count == 0) _profileList.Children.Add(Hint(T("Brak profili — wybierz grę poniżej i kliknij „+ Dodaj profil”.")));
        foreach (var prof in _cfg.Profiles.ToList())
        {
            var box = new StackPanel();
            bool active = _app.ActiveProfile == prof;
            box.Children.Add(Row(
                new TextBlock { Text = "🎮 " + prof.Game + (active ? T("  (aktywny)") : ""), Foreground = active ? AccentB : Fg, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), MinWidth = 160 },
                Btn("Zapisz bieżącą pozycję", () =>
                {
                    var (l, t) = _app.OverlayPosition;
                    prof.Left = l; prof.Top = t;
                    Changed();
                    Info(T("Zapisano pozycję nakładki w profilu „{0}”.", prof.Game));
                }),
                Btn("Usuń", () => { _cfg.Profiles.Remove(prof); Changed(); RebuildProfiles(); })));

            var theme = new ComboBox { MinWidth = 170 };
            theme.Items.Add(T("(motyw główny)"));
            foreach (var t in Theme.All) theme.Items.Add(t.Name);
            theme.SelectedItem = prof.Theme == "" ? theme.Items[0] : Theme.ByName(prof.Theme).Name;
            theme.SelectionChanged += (_, _) => { prof.Theme = theme.SelectedIndex <= 0 ? "" : (string)theme.SelectedItem; Changed(); };

            var scale = new Slider { Minimum = 60, Maximum = 200, Value = prof.Scale * 100, Width = 110, IsSnapToTickEnabled = true, TickFrequency = 5, VerticalAlignment = VerticalAlignment.Center };
            var scaleT = Label($"{prof.Scale * 100:0}%", 46, dim: true);
            scale.ValueChanged += (_, _) => { prof.Scale = scale.Value / 100; scaleT.Text = $"{scale.Value:0}%"; Changed(); };
            var op = new Slider { Minimum = 30, Maximum = 100, Value = prof.Opacity * 100, Width = 110, IsSnapToTickEnabled = true, TickFrequency = 5, VerticalAlignment = VerticalAlignment.Center };
            var opT = Label($"{prof.Opacity * 100:0}%", 46, dim: true);
            op.ValueChanged += (_, _) => { prof.Opacity = op.Value / 100; opT.Text = $"{op.Value:0}%"; Changed(); };

            var line2 = Row(Label(T("motyw"), dim: true), theme, Label(T("rozmiar"), dim: true), scale, scaleT, Label(T("krycie"), dim: true), op, opT);
            line2.Margin = new Thickness(20, 0, 0, 0);
            box.Children.Add(line2);
            var card = Card_(box);
            card.Margin = new Thickness(0, 4, 0, 4);
            _profileList.Children.Add(card);
        }
    }

    void RefreshGames() => _gamesList.ItemsSource = _cfg.Games.ToList();
    void RefreshProcesses() => _procCombo.ItemsSource = GameDetector.WindowedProcesses();

    // =====================================================================
    // Powiadomienia i dźwięki
    // =====================================================================

    ProgressBar _micLevel = null!;
    Rectangle _micThreshold = null!;

    UIElement NotifyTab()
    {
        var p = Page();
        p.Children.Add(Header("Wiadomości i szturchnięcia"));
        p.Children.Add(Check("Wiadomości prywatne", () => _cfg.ShowPrivateMessages, v => _cfg.ShowPrivateMessages = v));
        p.Children.Add(Check("Wiadomości na kanale", () => _cfg.ShowChannelMessages, v => _cfg.ShowChannelMessages = v));
        p.Children.Add(Check("Wiadomości do całego serwera", () => _cfg.ShowServerMessages, v => _cfg.ShowServerMessages = v));
        p.Children.Add(Check("Szturchnięcia (poke)", () => _cfg.ShowPokes, v => _cfg.ShowPokes = v));

        p.Children.Add(Header("Dźwięki"));
        p.Children.Add(SliderRow("Głośność", 0, 100, () => _cfg.Volume, v => _cfg.Volume = (int)v, v => $"{v:0}%"));
        p.Children.Add(Hint(T("Każdemu zdarzeniu możesz przypisać własny dźwięk: kliknij „Wybierz…” albo przeciągnij plik (.wav, .mp3, .wma, .aiff, .m4a) na wiersz. ") +
                            T("Program kopiuje plik do swojego folderu, a odtwarza najwyżej {0} s.", Sounds.MaxSeconds)));
        p.Children.Add(SoundRow("Gdy ktoś wchodzi na kanał", () => _cfg.SoundOnJoin, v => _cfg.SoundOnJoin = v, SoundKind.Join));
        p.Children.Add(SoundRow("Gdy ktoś wychodzi z kanału", () => _cfg.SoundOnLeave, v => _cfg.SoundOnLeave = v, SoundKind.Leave));
        p.Children.Add(SoundRow("Wiadomość", () => _cfg.SoundOnMessage, v => _cfg.SoundOnMessage = v, SoundKind.Message));
        p.Children.Add(SoundRow("Szturchnięcie", () => _cfg.SoundOnPoke, v => _cfg.SoundOnPoke = v, SoundKind.Poke));
        p.Children.Add(SoundRow("Wejście ulubionego (domyślny)", null, null, SoundKind.Favorite));
        p.Children.Add(SoundRow("Mówisz do wyciszonego mikrofonu", () => _cfg.MuteWarningSound, v => _cfg.MuteWarningSound = v, SoundKind.MuteWarning));
        p.Children.Add(Row(Btn("Otwórz folder dźwięków", () =>
        {
            System.IO.Directory.CreateDirectory(Sounds.Dir);
            Process.Start(new ProcessStartInfo("explorer.exe", Sounds.Dir));
        })));

        p.Children.Add(Header("Lektor (czytanie na głos)"));
        p.Children.Add(Hint("Lektor Windows czyta powiadomienia na głos — słychać go także w grach z wyłącznym pełnym ekranem, gdzie nakładki nie widać."));
        p.Children.Add(Check("Włącz lektora", () => _cfg.TtsEnabled, v => { _cfg.TtsEnabled = v; if (!v) _app.Voice.Stop(); }));
        var ttsKinds = Row(
            Check("wejścia", () => _cfg.TtsJoin, v => _cfg.TtsJoin = v),
            Check("wyjścia", () => _cfg.TtsLeave, v => _cfg.TtsLeave = v),
            Check("wiadomości", () => _cfg.TtsMessages, v => _cfg.TtsMessages = v),
            Check("szturchnięcia", () => _cfg.TtsPokes, v => _cfg.TtsPokes = v),
            Check("znajomi i obserwowane kanały", () => _cfg.TtsFriends, v => _cfg.TtsFriends = v));
        foreach (var c in ttsKinds.Children.OfType<CheckBox>()) c.Margin = new Thickness(0, 4, 14, 4);
        ttsKinds.Margin = new Thickness(20, 0, 0, 0);
        p.Children.Add(ttsKinds);
        p.Children.Add(Check("Czytaj tylko, gdy gra jest na pierwszym planie", () => _cfg.TtsOnlyInGame, v => _cfg.TtsOnlyInGame = v));
        var voice = new ComboBox { MinWidth = 260 };
        var voices = Speech.Voices();
        voice.Items.Add(T("Automatycznie (w języku programu)"));
        foreach (var v in voices) voice.Items.Add(v);
        voice.SelectedIndex = Math.Max(0, voices.IndexOf(_cfg.TtsVoice) + 1);
        voice.SelectionChanged += (_, _) => { _cfg.TtsVoice = voice.SelectedIndex <= 0 ? "" : voices[voice.SelectedIndex - 1]; Changed(); };
        p.Children.Add(Row(Label("Głos", 100), voice, Btn("▶ Test", () => _app.Voice.Say(T("{0} wchodzi", "Kamil") + ". " + T("{0} pisze: {1}", "Ola", T("idziemy na B?"))))));
        p.Children.Add(SliderRow("Tempo mowy", -5, 8, () => _cfg.TtsRate, v => _cfg.TtsRate = (int)v, v => $"{v:+0;-0;0}"));
        p.Children.Add(SliderRow("Głośność lektora", 0, 100, () => _cfg.TtsVolume, v => _cfg.TtsVolume = (int)v, v => $"{v:0}%"));

        p.Children.Add(Header("Znajomi"));
        p.Children.Add(Check("Powiadamiaj, gdy ulubiony wchodzi na serwer („★ … jest online”)", () => _cfg.FriendOnline, v => _cfg.FriendOnline = v));
        p.Children.Add(Check("Powiadamiaj, gdy ulubiony wychodzi z serwera", () => _cfg.FriendOffline, v => _cfg.FriendOffline = v));
        p.Children.Add(Hint("Ulubionych dodajesz w zakładce „Osoby”."));

        p.Children.Add(Header("Mikrofon wyciszony"));
        p.Children.Add(Check("Pokazuj czerwony pasek, gdy mikrofon lub głośniki są wyciszone", () => _cfg.MuteWarning, v => _cfg.MuteWarning = v));
        p.Children.Add(Check("Ostrzegaj, gdy mówisz do wyciszonego mikrofonu (pasek miga)", () => _cfg.MuteVoiceDetect, v => _cfg.MuteVoiceDetect = v));
        p.Children.Add(SliderRow("Czułość (niżej = czulej)", 1, 60, () => _cfg.MuteSensitivity, v => _cfg.MuteSensitivity = (int)v, v => $"{v:0}"));

        _micLevel = new ProgressBar { Width = 300, Height = 14, Minimum = 0, Maximum = 100, Foreground = AccentB, Background = InputBg, BorderBrush = Line };
        _micThreshold = new Rectangle { Width = 2, Height = 18, Fill = Brushes.IndianRed, HorizontalAlignment = HorizontalAlignment.Left };
        var meter = new Grid { Width = 300 };
        meter.Children.Add(_micLevel);
        meter.Children.Add(_micThreshold);
        var testBtn = new ToggleButton { Content = T("Test mikrofonu"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(10, 0, 0, 0) };
        testBtn.Checked += (_, _) => _app.MicTest = true;
        testBtn.Unchecked += (_, _) => _app.MicTest = false;
        p.Children.Add(Row(meter, testBtn));
        p.Children.Add(Hint("Mów normalnie w czasie testu: zielony pasek powinien przekraczać czerwoną kreskę, a szum tła nie. " +
                            "Mikrofon jest nasłuchiwany tylko wtedy, gdy masz go wyciszonego w TS (albo w czasie testu); dźwięk nie jest nigdzie zapisywany."));
        return Scroll(p);
    }

    /// <summary>Wiersz zdarzenia: włącz/wyłącz, test, przypisanie własnego pliku (przycisk lub przeciągnij i upuść).</summary>
    UIElement SoundRow(string label, Func<bool>? get, Action<bool>? set, SoundKind kind)
    {
        UIElement head;
        if (get != null && set != null)
        {
            head = Check(label, get, set);
        }
        else head = new TextBlock { Text = label, Foreground = Fg, Margin = new Thickness(22, 4, 0, 4) };

        var file = new TextBlock { MaxWidth = 260, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        void Show()
        {
            var f = Sounds.AssignedFile(kind);
            file.Text = f == null ? T("dźwięk wbudowany") : $"♪ {System.IO.Path.GetFileName(f)} ({Sounds.Length(f).TotalSeconds:0.#} s)";
            file.Foreground = f == null ? Dim : AccentB;
            file.ToolTip = f;
        }
        Show();

        void Assign(string source)
        {
            try
            {
                var (path, len) = Sounds.Import(source, kind.ToString().ToLowerInvariant());
                _cfg.SoundFiles[kind.ToString()] = path;
                Changed();
                Show();
                Sounds.Play(kind);
                if (len.TotalSeconds > Sounds.MaxSeconds)
                    Info(T("Plik trwa {0:0} s — przy powiadomieniu zagra tylko pierwsze {1:0} s.", len.TotalSeconds, Sounds.MaxSeconds));
            }
            catch (Exception ex) { Info(T("Nie udało się wczytać dźwięku: {0}", ex.Message)); }
        }

        var buttons = Row(
            Btn("▶", () => Sounds.Play(kind)),
            Btn("Wybierz…", () =>
            {
                var d = new Microsoft.Win32.OpenFileDialog
                {
                    Title = T("Dźwięk: {0}", T(label)),
                    Filter = T("Dźwięki") + " (*.wav;*.mp3;*.wma;*.aiff;*.m4a)|*.wav;*.mp3;*.wma;*.aiff;*.m4a",
                };
                if (d.ShowDialog(this) == true) Assign(d.FileName);
            }),
            Btn("Wbudowany", () =>
            {
                if (_cfg.SoundFiles.Remove(kind.ToString(), out var old)) Sounds.TryDelete(old);
                Changed();
                Show();
            }),
            file);
        buttons.Margin = new Thickness(22, 0, 0, 8);

        var row = new StackPanel();
        row.Children.Add(head);
        row.Children.Add(buttons);
        row.AllowDrop = true;
        row.Background = Brushes.Transparent;   // żeby przeciąganie działało na całej szerokości
        row.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        row.Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) Assign(files[0]);
        };
        return row;
    }

    // =====================================================================
    // Bindy: klawisz → dźwięk
    // =====================================================================

    StackPanel _bindList = null!;
    SoundBind? _capturing;
    Button? _captureBtn;

    UIElement BindsTab()
    {
        var p = Page();
        p.Children.Add(Header("Bindy klawiszowe"));
        p.Children.Add(Hint("Przypisz klawisz albo skrót (np. F9, Ctrl+1, Num 5) do dźwięku. Działa w grze i w każdym innym programie. " +
                            "Ponowne naciśnięcie w trakcie odtwarzania zatrzymuje dźwięk. Plik możesz przeciągnąć na wiersz binda."));
        p.Children.Add(Row(Btn("+ Dodaj bind", () =>
        {
            var b = new SoundBind { Name = $"Bind {_cfg.Binds.Count + 1}" };
            _cfg.Binds.Add(b);
            Changed();
            RebuildBinds();
        }, primary: true), Btn("■ Zatrzymaj wszystkie dźwięki", () => _app.Binds.StopAll())));
        _bindList = new StackPanel();
        p.Children.Add(_bindList);

        p.Children.Add(Header("Klawisz „zatrzymaj wszystko”"));
        p.Children.Add(Hint("Jeden skrót, który od razu ucina każdy grający dźwięk: klip z binda i sygnał powiadomienia. Przydaje się, gdy puścisz długi dźwięk i chcesz go uciszyć w trakcie gry."));
        _stopRow = new ContentControl();
        p.Children.Add(_stopRow);

        p.Children.Add(Header("Klawisze podglądu"));
        p.Children.Add(Hint("Przytrzymaj klawisz, a nakładka pokaże pełną listę osób na serwerze albo ostatnie 10 wiadomości. Po puszczeniu klawisza wszystko wraca. " +
                            "Działa nawet, gdy nakładka jest ukryta lub zbladła."));
        _peekServerRow = new ContentControl();
        _peekMsgRow = new ContentControl();
        p.Children.Add(_peekServerRow);
        p.Children.Add(_peekMsgRow);

        p.Children.Add(Header("Gdzie grać dźwięki bindów"));
        var dev = new ComboBox { MinWidth = 300 };
        var devices = BindManager.OutputDevices();
        dev.Items.Add(T("Domyślne urządzenie (słyszysz tylko Ty)"));
        foreach (var d in devices) dev.Items.Add(d.name);
        dev.SelectedIndex = Math.Max(0, devices.FindIndex(d => d.name == _cfg.BindDeviceName) + 1);
        dev.SelectionChanged += (_, _) =>
        {
            _cfg.BindDeviceName = dev.SelectedIndex <= 0 ? "" : devices[dev.SelectedIndex - 1].name;
            Changed();
        };
        p.Children.Add(Row(Label("Urządzenie", 100), dev));
        p.Children.Add(Check("Odtwarzaj też u mnie (gdy wybrano inne urządzenie)", () => _cfg.BindAlsoLocal, v => _cfg.BindAlsoLocal = v));
        p.Children.Add(Hint("Żeby dźwięki słyszeli inni na TeamSpeaku, potrzebny jest wirtualny kabel audio, np. VB-Audio Virtual Cable. " +
                            "Wybierz tu jego wejście (np. „CABLE Input”) i podaj je do TS razem z mikrofonem, np. przez VoiceMeeter. " +
                            "Bez tego dźwięki słyszysz tylko Ty."));
        RebuildBinds();
        return Scroll(p);
    }

    ContentControl? _stopRow, _peekServerRow, _peekMsgRow;

    void RebuildStopRow()
    {
        if (_stopRow == null) return;
        _stopRow.Content = SpecialKeyRow(_cfg.StopBind, T("Skrót:"), T("Nie przypisano — kliknij przycisk i naciśnij np. Pause albo Ctrl+Alt+0."));
        _peekServerRow!.Content = SpecialKeyRow(_cfg.PeekServerBind, T("Serwer:"), T("Nie przypisano — np. Tab nie zadziała (gra go używa), spróbuj Ctrl+Tab albo `."));
        _peekMsgRow!.Content = SpecialKeyRow(_cfg.PeekMessagesBind, T("Wiadomości:"), T("Nie przypisano — np. Ctrl+M."));
    }

    /// <summary>Skrót „specjalny” (zatrzymanie, podgląd): przycisk nagrywania, usuwanie, ostrzeżenia o kolizjach.</summary>
    UIElement SpecialKeyRow(SoundBind s, string label, string emptyHint)
    {
        var keyBtn = Btn(s.KeyText, () => { });
        keyBtn.MinWidth = 120;
        keyBtn.ToolTip = T("Kliknij i naciśnij klawisz lub skrót");
        keyBtn.Click += (_, _) => StartCapture(s, keyBtn);
        var row = Row(Label(label, 100), keyBtn, Btn("Usuń skrót", () => { s.Key = 0; s.Modifiers = 0; Changed(); RebuildBinds(); }));
        var box = new StackPanel();
        box.Children.Add(row);
        var specials = new[] { _cfg.StopBind, _cfg.PeekServerBind, _cfg.PeekMessagesBind };
        string? warn = _app.Binds.Failed.Contains(s.Id) ? T("Skrót {0} jest zajęty przez inny program — wybierz inny.", s.KeyText)
            : s.Key != 0 && _cfg.Binds.Any(b => b.Enabled && b.Key == s.Key && b.Modifiers == s.Modifiers) ? T("Ten sam skrót ma jeden z bindów.")
            : s.Key != 0 && specials.Any(o => o != s && o.Key == s.Key && o.Modifiers == s.Modifiers) ? T("Ten sam skrót ma inny klawisz specjalny.")
            : s.Key == 0 ? emptyHint : null;
        if (warn != null)
            box.Children.Add(new TextBlock { Text = warn, Foreground = s.Key == 0 ? Dim : Brushes.IndianRed, FontSize = 12, Margin = new Thickness(100, 2, 0, 0), TextWrapping = TextWrapping.Wrap });
        return box;
    }

    void RebuildBinds()
    {
        RebuildStopRow();
        if (_bindList == null) return;
        _bindList.Children.Clear();
        if (_cfg.Binds.Count == 0) _bindList.Children.Add(Hint("Nie masz jeszcze żadnych bindów — kliknij „+ Dodaj bind”."));
        foreach (var b in _cfg.Binds.ToList()) _bindList.Children.Add(BindRow(b));
    }

    UIElement BindRow(SoundBind b)
    {
        var enabled = new CheckBox { IsChecked = b.Enabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), ToolTip = T("Włączony") };
        enabled.Checked += (_, _) => { b.Enabled = true; Changed(); RebuildBinds(); };
        enabled.Unchecked += (_, _) => { b.Enabled = false; Changed(); RebuildBinds(); };

        var name = Input(170);
        name.Text = b.Name;
        name.LostFocus += (_, _) => { var n = name.Text.Trim(); if (n != "" && n != b.Name) { b.Name = n; Changed(); } };

        var keyBtn = Btn(b.KeyText, () => { });
        keyBtn.MinWidth = 120;
        keyBtn.ToolTip = T("Kliknij i naciśnij klawisz lub skrót");
        keyBtn.Click += (_, _) => StartCapture(b, keyBtn);

        var line1 = Row(enabled, name, keyBtn,
            Btn("▶", () => _app.Binds.Trigger(b)),
            Btn("Usuń", () =>
            {
                if (!Confirm(T("Usunąć bind „{0}”?", b.Name))) return;
                _cfg.Binds.Remove(b);
                Sounds.TryDelete(b.SoundPath);
                Changed();
                RebuildBinds();
            }));

        var file = new TextBlock { MaxWidth = 230, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        bool hasFile = b.SoundPath != "" && System.IO.File.Exists(b.SoundPath);
        file.Text = hasFile ? $"♪ {System.IO.Path.GetFileName(b.SoundPath)} ({Sounds.Length(b.SoundPath).TotalSeconds:0.#} s)" : T("brak dźwięku");
        file.Foreground = hasFile ? AccentB : Brushes.IndianRed;

        void Assign(string source)
        {
            try
            {
                var (path, _) = Sounds.Import(source, "bind-" + b.Id);
                b.SoundPath = path;
                if (b.Name.StartsWith("Bind ")) b.Name = System.IO.Path.GetFileNameWithoutExtension(source);
                Changed();
                RebuildBinds();
                _app.Binds.Trigger(b);
            }
            catch (Exception ex) { Info(T("Nie udało się wczytać dźwięku: {0}", ex.Message)); }
        }

        var vol = new Slider { Minimum = 0, Maximum = 100, Value = b.Volume, Width = 110, VerticalAlignment = VerticalAlignment.Center, IsSnapToTickEnabled = true, TickFrequency = 5 };
        var volText = Label($"{b.Volume}%", 44, dim: true);
        vol.ValueChanged += (_, _) => { b.Volume = (int)vol.Value; volText.Text = $"{b.Volume}%"; _app.Cfg.Save(); };

        var line2 = Row(
            Btn("Wybierz dźwięk…", () =>
            {
                var d = new Microsoft.Win32.OpenFileDialog { Title = T("Dźwięk binda: {0}", b.Name), Filter = T("Dźwięki") + " (*.wav;*.mp3;*.wma;*.aiff;*.m4a)|*.wav;*.mp3;*.wma;*.aiff;*.m4a" };
                if (d.ShowDialog(this) == true) Assign(d.FileName);
            }),
            file, Label("głośność", dim: true), vol, volText);
        line2.Margin = new Thickness(26, 0, 0, 0);

        var box = new StackPanel();
        box.Children.Add(line1);
        box.Children.Add(line2);
        if (_app.Binds.Failed.Contains(b.Id))
            box.Children.Add(new TextBlock
            {
                Text = T("Skrót {0} jest zajęty przez inny program — wybierz inny.", b.KeyText),
                Foreground = Brushes.IndianRed, Margin = new Thickness(26, 2, 0, 0), FontSize = 12,
            });
        else if (b.Key != 0 && b.Enabled && _cfg.StopBind.Key == b.Key && _cfg.StopBind.Modifiers == b.Modifiers)
            box.Children.Add(new TextBlock
            {
                Text = T("Ten sam skrót ma klawisz „zatrzymaj wszystko”."), Foreground = Brushes.IndianRed, Margin = new Thickness(26, 2, 0, 0), FontSize = 12,
            });
        else if (_cfg.Binds.Any(o => o != b && o.Enabled && b.Enabled && o.Key == b.Key && o.Modifiers == b.Modifiers && b.Key != 0))
            box.Children.Add(new TextBlock
            {
                Text = T("Ten sam skrót ma inny bind."), Foreground = Brushes.IndianRed, Margin = new Thickness(26, 2, 0, 0), FontSize = 12,
            });

        var card = Card_(box);
        card.Margin = new Thickness(0, 4, 0, 4);
        card.Opacity = b.Enabled ? 1 : 0.55;
        card.AllowDrop = true;
        card.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        card.Drop += (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } f) Assign(f[0]); };
        return card;
    }

    void StartCapture(SoundBind b, Button btn)
    {
        CancelCapture();
        _capturing = b;
        _captureBtn = btn;
        btn.Content = T("Naciśnij skrót…  (Esc = anuluj, Backspace = usuń)");
        btn.Background = AccentB;
        btn.Foreground = Brushes.Black;
        _app.BindCapture = true;
        btn.Focus();
    }

    void CancelCapture()
    {
        if (_capturing == null) return;
        _capturing = null;
        _captureBtn = null;
        _app.BindCapture = false;
        RebuildBinds();
    }

    void OnCaptureKey(object sender, KeyEventArgs e)
    {
        if (_capturing == null) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;   // czekamy na właściwy klawisz
        var b = _capturing;
        if (key == Key.Escape) { CancelCapture(); return; }
        if (key is Key.Back or Key.Delete)
        {
            b.Key = 0; b.Modifiers = 0;
        }
        else
        {
            uint mods = 0;
            var m = Keyboard.Modifiers;
            if (m.HasFlag(ModifierKeys.Alt)) mods |= 1;
            if (m.HasFlag(ModifierKeys.Control)) mods |= 2;
            if (m.HasFlag(ModifierKeys.Shift)) mods |= 4;
            if (m.HasFlag(ModifierKeys.Windows)) mods |= 8;
            b.Key = KeyInterop.VirtualKeyFromKey(key);
            b.Modifiers = mods;
        }
        _capturing = null;
        _captureBtn = null;
        _app.BindCapture = false;   // rejestruje skróty od nowa (z nowym klawiszem)
        _app.Cfg.Save();
        RebuildBinds();
    }

    // =====================================================================
    // Osoby: ulubieni i ignorowani
    // =====================================================================

    StackPanel _peopleOnline = null!, _favList = null!, _ignList = null!;

    UIElement PeopleTab()
    {
        var p = Page();
        p.Children.Add(Header("Osoby na serwerze"));
        p.Children.Add(Hint("★ = ulubiony: wyróżniony kolor nicku i własny dźwięk wejścia. Ignorowani (np. boty muzyczne) znikają z nakładki i nie wywołują powiadomień."));
        p.Children.Add(Row(Btn("Odśwież", RebuildPeople)));
        _peopleOnline = new StackPanel();
        p.Children.Add(Card_(_peopleOnline));

        p.Children.Add(Header("Ulubieni"));
        _favList = new StackPanel();
        p.Children.Add(Card_(_favList));

        p.Children.Add(Header("Obserwowane kanały"));
        p.Children.Add(Hint("Nakładka pokazuje pod Twoim kanałem małą listę osób z wybranych kanałów (np. „Lobby”), żebyś widział, kto czeka, choć siedzisz gdzie indziej."));
        _watchList = new StackPanel();
        p.Children.Add(Card_(_watchList));
        var chCombo = new ComboBox { MinWidth = 240, IsEditable = true };
        chCombo.DropDownOpened += (_, _) => chCombo.ItemsSource = _app.Ts.ChannelNames();
        chCombo.ItemsSource = _app.Ts.ChannelNames();
        p.Children.Add(Row(chCombo, Btn("+ Obserwuj kanał", () =>
        {
            var n = (chCombo.Text ?? "").Trim();
            if (n == "" || _cfg.WatchedChannels.Any(w => w.Equals(n, StringComparison.CurrentCultureIgnoreCase))) return;
            _cfg.WatchedChannels.Add(n);
            Changed();
            RebuildPeople();
        })));
        p.Children.Add(Check("Ukrywaj obserwowany kanał, gdy nikogo na nim nie ma", () => _cfg.WatchedHideEmpty, v => _cfg.WatchedHideEmpty = v));
        p.Children.Add(Check("Powiadamiaj, gdy ktoś wchodzi na obserwowany kanał", () => _cfg.WatchedNotify, v => _cfg.WatchedNotify = v));

        p.Children.Add(Header("Ignorowani"));
        _ignList = new StackPanel();
        p.Children.Add(Card_(_ignList));
        RebuildPeople();
        return Scroll(p);
    }

    StackPanel? _watchList;

    void RebuildPeople()
    {
        if (_watchList != null)
        {
            _watchList.Children.Clear();
            if (_cfg.WatchedChannels.Count == 0) _watchList.Children.Add(Hint(T("Nie obserwujesz żadnego kanału.")));
            foreach (var w in _cfg.WatchedChannels.ToList())
                _watchList.Children.Add(Row(Label("👁 " + w, 260), Btn("Usuń", () => { _cfg.WatchedChannels.Remove(w); Changed(); RebuildPeople(); })));
        }
        if (_peopleOnline == null) return;
        _peopleOnline.Children.Clear();
        var all = _app.Ts?.AllClients() ?? new();
        if (all.Count == 0) _peopleOnline.Children.Add(Hint("Nikogo poza Tobą nie widać — połącz się z serwerem TeamSpeak i kliknij „Odśwież”."));
        foreach (var c in all)
        {
            bool fav = _cfg.FavoriteFor(c.Uid) != null, ign = _cfg.IsIgnored(c.Uid);
            var row = Row(Label(c.Nickname, 220),
                Btn(fav ? "★ Ulubiony" : "☆ Ulubiony", () =>
                {
                    if (fav) _cfg.Favorites.RemoveAll(f => f.Uid == c.Uid);
                    else
                    {
                        _cfg.IgnoredClients.RemoveAll(i => i.Uid == c.Uid);
                        _cfg.Favorites.Add(new Favorite { Uid = c.Uid, Nickname = c.Nickname });
                    }
                    RebuildPeople(); Changed();
                }),
                Btn(ign ? "Przestań ignorować" : "Ignoruj", () =>
                {
                    if (ign) _cfg.IgnoredClients.RemoveAll(i => i.Uid == c.Uid);
                    else
                    {
                        _cfg.Favorites.RemoveAll(f => f.Uid == c.Uid);
                        _cfg.IgnoredClients.Add(new Ignored { Uid = c.Uid, Nickname = c.Nickname });
                    }
                    RebuildPeople(); Changed();
                }));
            _peopleOnline.Children.Add(row);
        }

        _favList.Children.Clear();
        if (_cfg.Favorites.Count == 0) _favList.Children.Add(Hint("Nikogo jeszcze nie dodano."));
        foreach (var f in _cfg.Favorites.ToList())
        {
            var swatch = new Button { Width = 34, Height = 24, Margin = new Thickness(0, 0, 8, 0), Background = SafeBrush(f.Color), BorderBrush = Line, Style = null };
            swatch.Click += (_, _) =>
            {
                using var dlg = new Forms.ColorDialog { FullOpen = true };
                if (dlg.ShowDialog() != Forms.DialogResult.OK) return;
                f.Color = $"#FF{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
                RebuildPeople(); Changed();
            };
            string soundLabel = f.SoundPath == "" ? T("Dźwięk: wbudowany") : T("Dźwięk: {0}", System.IO.Path.GetFileName(f.SoundPath));
            _favList.Children.Add(Row(Label("★ " + f.Nickname, 200), swatch,
                Btn(soundLabel, () =>
                {
                    var d = new Microsoft.Win32.OpenFileDialog { Filter = T("Dźwięki") + " (*.wav;*.mp3;*.wma;*.aiff;*.m4a)|*.wav;*.mp3;*.wma;*.aiff;*.m4a", Title = T("Dźwięk wejścia: {0}", f.Nickname) };
                    if (d.ShowDialog(this) != true) return;
                    try
                    {
                        var key = "fav-" + string.Concat(f.Uid.Where(char.IsLetterOrDigit).Take(16));
                        var (path, _) = Sounds.Import(d.FileName, key);
                        f.SoundPath = path;
                        RebuildPeople(); Changed();
                        Sounds.Play(SoundKind.Favorite, path);
                    }
                    catch (Exception ex) { Info(T("Nie udało się wczytać dźwięku: {0}", ex.Message)); }
                }),
                Btn("▶", () => Sounds.Play(SoundKind.Favorite, f.SoundPath)),
                Btn("Wbudowany", () => { Sounds.TryDelete(f.SoundPath); f.SoundPath = ""; RebuildPeople(); Changed(); }),
                Btn("Usuń", () => { _cfg.Favorites.Remove(f); RebuildPeople(); Changed(); })));
        }

        _ignList.Children.Clear();
        if (_cfg.IgnoredClients.Count == 0) _ignList.Children.Add(Hint("Nikogo nie ignorujesz."));
        foreach (var i in _cfg.IgnoredClients.ToList())
            _ignList.Children.Add(Row(Label(i.Nickname, 220), Btn("Przestań ignorować", () => { _cfg.IgnoredClients.Remove(i); RebuildPeople(); Changed(); })));
    }

    static Brush SafeBrush(string hex) { try { return Theme.B(hex); } catch { return Brushes.Gold; } }

    // =====================================================================
    // OBS
    // =====================================================================

    TextBlock _obsStatus = null!;
    TextBox _obsUrl = null!;

    UIElement ObsTab()
    {
        var p = Page();
        p.Children.Add(Header("Nakładka dla streamu (OBS)"));
        p.Children.Add(Hint("Program udostępnia nakładkę jako stronę w Twoim komputerze. W OBS dodaj źródło „Przeglądarka” z tym adresem — " +
                            "widzowie zobaczą, kto mówi. Działa też przy grze w trybie pełnoekranowym."));
        p.Children.Add(Check("Włącz stronę dla OBS", () => _cfg.ObsEnabled, v => _cfg.ObsEnabled = v));
        var port = Input(80);
        port.Text = _cfg.ObsPort.ToString();
        p.Children.Add(Row(Label("Port", 60), port, Btn("Zastosuj port", () =>
        {
            if (int.TryParse(port.Text, out var v) && v is > 1024 and < 65536) { _cfg.ObsPort = v; Changed(); }
            else Info("Port musi być liczbą od 1025 do 65535.");
        })));
        _obsUrl = Input(320);
        _obsUrl.IsReadOnly = true;
        p.Children.Add(Row(_obsUrl,
            Btn("Kopiuj", () => { try { Clipboard.SetText(_obsUrl.Text); } catch { } }),
            Btn("Otwórz w przeglądarce", () => Process.Start(new ProcessStartInfo(_obsUrl.Text) { UseShellExecute = true }))));
        _obsStatus = Hint("");
        p.Children.Add(_obsStatus);

        p.Children.Add(Header("Ustawienia źródła w OBS"));
        p.Children.Add(Hint("Szerokość 500, wysokość 600, zaznacz „Odśwież przeglądarkę, gdy scena staje się aktywna”. Tło jest przezroczyste."));
        p.Children.Add(Hint("Dodatki do adresu:\n" +
                            "  ?only=talking — tylko osoby, które mówią\n" +
                            "  ?scale=1.5 — powiększenie\n" +
                            "  ?notices=0 — bez powiadomień\n" +
                            "Można łączyć: …/?only=talking&scale=1.5"));
        return Scroll(p);
    }

    // =====================================================================
    // Telefon i Discord
    // =====================================================================

    StackPanel _phoneInfo = null!;
    TextBlock _phoneStatus = null!, _discordStatus = null!;

    UIElement PhoneTab()
    {
        var p = Page();
        p.Children.Add(Header("Panel na telefonie"));
        p.Children.Add(Hint("Strona w Twojej sieci domowej: lista osób na kanale i serwerze, ostatnie wiadomości, przyciski bindów i „zatrzymaj wszystko”. " +
                            "Telefon musi być w tej samej sieci Wi-Fi co komputer."));
        p.Children.Add(Check("Włącz panel na telefonie", () => _cfg.PhoneEnabled, v => { _cfg.PhoneEnabled = v; Dispatcher.BeginInvoke(RebuildPhone); }));
        var port = Input(80);
        port.Text = _cfg.PhonePort.ToString();
        p.Children.Add(Row(Label("Port", 60), port, Btn("Zastosuj port", () =>
        {
            if (int.TryParse(port.Text, out var v) && v is > 1024 and < 65536 && v != _cfg.ObsPort) { _cfg.PhonePort = v; Changed(); RebuildPhone(); }
            else Info(T("Port musi być liczbą od 1025 do 65535 i inny niż port OBS."));
        })));
        _phoneInfo = new StackPanel();
        p.Children.Add(_phoneInfo);
        _phoneStatus = Hint("");
        p.Children.Add(_phoneStatus);
        p.Children.Add(Hint("Przy pierwszym włączeniu Windows zapyta o dostęp do sieci — zaznacz „Sieci prywatne” i kliknij „Zezwól”. " +
                            "Adres zawiera tajny klucz: bez niego nikt w sieci nie odpali Twoich dźwięków. Jeśli ktoś go poznał, kliknij „Nowy klucz”."));
        RebuildPhone();

        p.Children.Add(Header("Status w Discordzie"));
        p.Children.Add(Hint("Twój profil Discord pokaże np. „Na TeamSpeaku: Pluton 6 · 3 osób na kanale” z licznikiem czasu. Wymaga uruchomionej aplikacji Discord na tym komputerze."));
        p.Children.Add(Check("Pokazuj status w Discordzie", () => _cfg.DiscordEnabled, v => _cfg.DiscordEnabled = v));
        var appId = Input(220);
        appId.Text = _cfg.DiscordAppId;
        p.Children.Add(Row(Label("Application ID", 120), appId, Btn("Zastosuj", () =>
        {
            var id = appId.Text.Trim();
            if (id != "" && !id.All(char.IsDigit)) { Info(T("Application ID to sam ciąg cyfr, np. 1234567890123456789.")); return; }
            _cfg.DiscordAppId = id;
            Changed();
        }), Btn("Otwórz portal Discord", () => Process.Start(new ProcessStartInfo("https://discord.com/developers/applications") { UseShellExecute = true }))));
        p.Children.Add(Hint("Jak zdobyć Application ID (jednorazowo, ok. 1 minuty):\n" +
                            "1. Otwórz portal Discord i zaloguj się.\n" +
                            "2. Kliknij „New Application” i nadaj nazwę, np. „TeamSpeak” — Discord pokaże ją jako „Gra w TeamSpeak”.\n" +
                            "3. Skopiuj „Application ID” z zakładki „General Information”, wklej powyżej i kliknij „Zastosuj”."));
        p.Children.Add(Check("Pokazuj nazwę kanału", () => _cfg.DiscordShowChannel, v => _cfg.DiscordShowChannel = v));
        p.Children.Add(Check("Pokazuj nazwę serwera", () => _cfg.DiscordShowServer, v => _cfg.DiscordShowServer = v));
        var img = Input(220);
        img.Text = _cfg.DiscordLargeImage;
        p.Children.Add(Row(Label("Obrazek (opcjonalnie)", 160), img, Btn("Zastosuj", () => { _cfg.DiscordLargeImage = img.Text.Trim(); Changed(); })));
        p.Children.Add(Hint("Nazwa obrazka dodanego w portalu (Rich Presence → Art Assets) albo adres https do obrazka."));
        _discordStatus = Hint("");
        p.Children.Add(_discordStatus);

        p.Children.Add(Header("Podświetlenie klawiatury i myszy (RGB)"));
        p.Children.Add(Hint("Błysk kolorem przy wybranych zdarzeniach — działa przez darmowy program OpenRGB, który obsługuje sprzęt wielu marek naraz. " +
                            "Po błysku podświetlenie wraca do Twoich ustawień."));
        p.Children.Add(Check("Włącz błyski RGB", () => _cfg.RgbEnabled, v => { _cfg.RgbEnabled = v; if (v) _ = ProbeRgb(); }));
        foreach (var (kind, label) in new[] { ("Poke", "Szturchnięcie"), ("Friend", "Znajomy jest online"), ("Join", "Ktoś wchodzi na kanał"),
                                              ("Message", "Wiadomość"), ("MuteWarning", "Mówisz do wyciszonego mikrofonu") })
            p.Children.Add(RgbRow(kind, label));
        p.Children.Add(SliderRow("Liczba błysków", 1, 6, () => _cfg.RgbFlashes, v => _cfg.RgbFlashes = (int)v, v => $"{v:0}"));
        p.Children.Add(SliderRow("Długość błysku", 80, 500, () => _cfg.RgbFlashMs, v => _cfg.RgbFlashMs = (int)v, v => $"{v:0} ms"));
        _rgbStatus = Hint("");
        p.Children.Add(Row(Btn("Sprawdź połączenie z OpenRGB", () => _ = ProbeRgb()),
            Btn("Pobierz OpenRGB", () => Process.Start(new ProcessStartInfo("https://openrgb.org") { UseShellExecute = true }))));
        p.Children.Add(_rgbStatus);
        p.Children.Add(Hint("W OpenRGB otwórz kartę „SDK Server” i kliknij „Start Server” (oraz w ustawieniach OpenRGB włącz uruchamianie serwera przy starcie). " +
                            "Błyskają tylko urządzenia widoczne w OpenRGB."));
        if (_cfg.RgbEnabled) _ = ProbeRgb();
        return Scroll(p);
    }

    TextBlock? _rgbStatus;

    async Task ProbeRgb()
    {
        if (_rgbStatus == null) return;
        _rgbStatus.Text = T("Sprawdzam…");
        bool ok = await _app.Lights.Probe();
        _rgbStatus.Text = _app.Lights.Status + (ok ? "\n" + T("Urządzenia: {0}", string.Join(", ", _app.Lights.Devices.Select(d => $"{d.Name} ({d.Leds})"))) : "");
    }

    UIElement RgbRow(string kind, string label)
    {
        _cfg.RgbColors.TryGetValue(kind, out var current);
        current ??= "";
        var on = new CheckBox { Content = T(label), IsChecked = current != "", Foreground = Fg, Width = 270, VerticalAlignment = VerticalAlignment.Center };
        string last = current != "" ? current : kind switch { "Poke" => "#FFFF8C00", "Friend" => "#FF00FF66", "MuteWarning" => "#FFFF0000", "Join" => "#FF00A0FF", _ => "#FFFFFFFF" };
        var swatch = new Button { Width = 34, Height = 24, Margin = new Thickness(0, 0, 8, 0), Background = Theme.B(last), BorderBrush = Line, Style = null };
        void Save() { _cfg.RgbColors[kind] = on.IsChecked == true ? last : ""; Changed(); }
        on.Checked += (_, _) => Save();
        on.Unchecked += (_, _) => Save();
        swatch.Click += (_, _) =>
        {
            var c = Theme.C(last);
            using var dlg = new Forms.ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B) };
            if (dlg.ShowDialog() != Forms.DialogResult.OK) return;
            last = $"#FF{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
            swatch.Background = Theme.B(last);
            if (on.IsChecked == true) Save();
        };
        return Row(on, swatch, Btn("▶ Test", () =>
        {
            if (!_cfg.RgbEnabled) { Info(T("Najpierw zaznacz „Włącz błyski RGB”.")); return; }
            _app.Lights.Flash(last);
        }));
    }

    void RebuildPhone()
    {
        if (_phoneInfo == null) return;
        _phoneInfo.Children.Clear();
        if (!_cfg.PhoneEnabled) return;
        var ips = PhoneServer.LocalAddresses();
        if (ips.Count == 0) { _phoneInfo.Children.Add(Hint(T("Nie znaleziono połączenia z siecią lokalną."))); return; }
        var url = _app.PhoneUrl(ips[0]);
        var qr = new System.Windows.Controls.Image { Width = 220, Height = 220, Margin = new Thickness(0, 6, 16, 6), Source = Qr(url) };
        RenderOptions.SetBitmapScalingMode(qr, BitmapScalingMode.NearestNeighbor);
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(Label("Zeskanuj kod aparatem telefonu albo wpisz adres:"));
        var box = Input(380);
        box.Text = url;
        box.IsReadOnly = true;
        right.Children.Add(Row(box));
        right.Children.Add(Row(
            Btn("Kopiuj", () => { try { Clipboard.SetText(url); } catch { } }),
            Btn("Otwórz tutaj", () => Process.Start(new ProcessStartInfo(_app.PhoneUrl("localhost")) { UseShellExecute = true })),
            Btn("Nowy klucz", () =>
            {
                if (!Confirm(T("Wygenerować nowy klucz? Stary adres przestanie działać — trzeba będzie zeskanować kod jeszcze raz."))) return;
                _app.RegeneratePhoneKey();
                RebuildPhone();
            })));
        if (ips.Count > 1) right.Children.Add(Hint(T("Inne adresy tego komputera: {0}", string.Join(", ", ips.Skip(1)))));
        right.Children.Add(Hint("Wskazówka: w przeglądarce telefonu wybierz „Dodaj do ekranu głównego” — panel otworzy się jak aplikacja."));
        var row = new WrapPanel();
        row.Children.Add(new Border { Background = Brushes.White, Padding = new Thickness(6), Child = qr, Margin = new Thickness(0, 6, 16, 6) });
        row.Children.Add(right);
        _phoneInfo.Children.Add(row);
    }

    static System.Windows.Media.Imaging.BitmapImage Qr(string text)
    {
        using var gen = new QRCoder.QRCodeGenerator();
        using var data = gen.CreateQrCode(text, QRCoder.QRCodeGenerator.ECCLevel.M);
        var png = new QRCoder.PngByteQRCode(data).GetGraphic(8);
        var bmp = new System.Windows.Media.Imaging.BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource = new System.IO.MemoryStream(png);
        bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    // =====================================================================
    // Aktualizacje i instalacja
    // =====================================================================

    TextBlock _updStatus = null!, _instStatus = null!;

    UIElement UpdatesTab()
    {
        var p = Page();
        p.Children.Add(Header("Wersja"));
        p.Children.Add(Label(T("Wersja programu: {0}", Updater.Current)));
        p.Children.Add(Label(T("Autor: {0}", "AKI76PL") + "  ·  © 2026 AKI76PL  ·  " + T("licencja MIT"), dim: true));
        p.Children.Add(Check("Sprawdzaj aktualizacje przy starcie", () => _cfg.AutoUpdateCheck, v => _cfg.AutoUpdateCheck = v));
        _updStatus = Hint("");
        p.Children.Add(Row(Btn("Sprawdź teraz", CheckNow, primary: true),
            Btn("Strona wydań", () => Process.Start(new ProcessStartInfo(Updater.ReleasesPage) { UseShellExecute = true }))));
        p.Children.Add(_updStatus);

        p.Children.Add(Header("Kopia ustawień"));
        p.Children.Add(Hint("Jeden plik z wszystkimi ustawieniami: motywy, dźwięki, bindy, ulubieni, profile gier. Przydaje się przy zmianie komputera. " +
                            "Zgody TeamSpeaka nie da się przenieść — na nowym komputerze kliknij „Zezwól” jeszcze raz."));
        p.Children.Add(Row(
            Btn("Eksportuj ustawienia…", () =>
            {
                var d = new Microsoft.Win32.SaveFileDialog { Filter = T("Kopia TS6 Overlay") + " (*.ts6backup)|*.ts6backup", FileName = $"TS6Overlay-{DateTime.Now:yyyy-MM-dd}{Backup.Extension}" };
                if (d.ShowDialog(this) != true) return;
                try { _app.ExportBackup(d.FileName); Info(T("Zapisano kopię ustawień.")); }
                catch (Exception ex) { Info(T("Nie udało się zapisać: {0}", ex.Message)); }
            }),
            Btn("Importuj ustawienia…", () =>
            {
                var d = new Microsoft.Win32.OpenFileDialog { Filter = T("Kopia TS6 Overlay") + " (*.ts6backup)|*.ts6backup" };
                if (d.ShowDialog(this) != true) return;
                if (!Confirm(T("Wczytać ustawienia z kopii? Obecne ustawienia zostaną zastąpione, a program uruchomi się ponownie."))) return;
                try { _app.ImportBackup(d.FileName); }
                catch (Exception ex) { Info(T("Nie udało się wczytać kopii: {0}", ex.Message)); }
            })));

        p.Children.Add(Header("Instalacja"));
        _instStatus = Hint("");
        p.Children.Add(_instStatus);
        p.Children.Add(Row(
            Btn("Zainstaluj na tym komputerze", () =>
            {
                if (Installer.IsInstalledCopy) { Info("Ta kopia jest już zainstalowana."); return; }
                if (Confirm("Zainstalować TS6 Overlay w %LOCALAPPDATA%\\Programs\\TS6Overlay (skrót w menu Start, autostart)?\nProgram uruchomi się ponownie z nowego miejsca."))
                    _app.InstallNow();
            }),
            Btn("Odinstaluj…", () =>
            {
                if (!Installer.IsInstalled) { Info("Program nie jest zainstalowany — wystarczy usunąć plik."); return; }
                Process.Start(new ProcessStartInfo(Installer.InstalledExe, "--uninstall") { UseShellExecute = true });
            })));
        p.Children.Add(Hint("Znajomym wyślij link do strony wydań albo sam plik TS6Overlay.exe. Przy pierwszym uruchomieniu program zaproponuje instalację, " +
                            "a potem sam będzie się aktualizował."));
        UpdateInstallStatus();
        return Scroll(p);
    }

    void UpdateInstallStatus()
    {
        _instStatus.Text = Installer.IsInstalledCopy ? T("Zainstalowany w {0}.", Installer.InstallDir)
            : Installer.IsInstalled ? T("Zainstalowana kopia jest w {0}, ale uruchomiona jest ta: {1}", Installer.InstallDir, Environment.ProcessPath)
            : T("Nie zainstalowano — program działa z: {0}", Environment.ProcessPath);
    }

    async void CheckNow()
    {
        _updStatus.Text = T("Sprawdzam…");
        try
        {
            var r = await Updater.CheckAsync();
            if (r == null) { _updStatus.Text = T("Masz najnowszą wersję ({0}).", Updater.Current); return; }
            _updStatus.Text = T("Dostępna wersja {0}.", r.Version);
            await _app.AskAndUpdate(r);
        }
        catch (Exception ex) { _updStatus.Text = T("Nie udało się sprawdzić: {0}", ex.Message); }
    }

    // =====================================================================
    // Odświeżanie (poziom mikrofonu, status OBS)
    // =====================================================================

    void TimerTick()
    {
        if (_phoneStatus != null)
            _phoneStatus.Text = !_cfg.PhoneEnabled ? "" : _app.Phone.Running ? T("Panel działa.") : T("Nie udało się uruchomić: {0} Spróbuj innego portu.", _app.Phone.Error ?? "");
        if (_discordStatus != null)
            _discordStatus.Text = !_cfg.DiscordEnabled ? T("Wyłączone.") : _cfg.DiscordAppId == "" ? T("Podaj Application ID.") : _app.Discord.Status;
        if (_micLevel != null)
        {
            _micLevel.Value = Math.Min(100, _app.Mic.Level * 100);
            _micThreshold.Margin = new Thickness(Math.Clamp(_cfg.MuteSensitivity, 1, 100) / 100.0 * 300, 0, 0, 0);
        }
        if (_obsUrl != null)
        {
            _obsUrl.Text = $"http://localhost:{_cfg.ObsPort}/";
            _obsStatus.Text = !_cfg.ObsEnabled ? T("Wyłączone.")
                : _app.ObsRunning ? T("Działa — wklej adres do OBS.")
                : T("Nie udało się uruchomić: {0} Spróbuj innego portu.", _app.Obs.Error ?? T("port zajęty?"));
        }
    }

    // =====================================================================
    // Budowanie interfejsu
    // =====================================================================

    static TabItem Tab(string header, UIElement content) => new() { Header = T(header), Content = content };

    static StackPanel Page() => new() { Margin = new Thickness(16, 8, 16, 16) };

    ScrollViewer Scroll(UIElement e) => new() { Content = e, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Card };

    TextBlock Header(string text) => new()
    {
        Text = T(text), FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = AccentB, Margin = new Thickness(0, 16, 0, 6),
    };

    TextBlock Label(string text, double width = double.NaN, bool dim = false) => new()
    {
        Text = T(text), Width = width, Foreground = dim ? Dim : Fg, VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis,
    };

    TextBlock Hint(string text) => new()
    {
        Text = T(text), Foreground = Dim, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4),
    };

    static WrapPanel Row(params UIElement[] items)
    {
        var r = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        foreach (var i in items) r.Children.Add(i);
        return r;
    }

    Border Card_(UIElement e) => new()
    {
        Child = e, Background = Bg, BorderBrush = Line, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 6),
    };

    Button Btn(string text, Action click, bool primary = false)
    {
        var b = new Button { Content = T(text), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0), MinHeight = 26 };
        if (primary) { b.Background = AccentB; b.Foreground = Brushes.Black; b.FontWeight = FontWeights.SemiBold; }
        b.Click += (_, _) => click();
        return b;
    }

    TextBox Input(double width) => new()
    {
        Width = width, Background = InputBg, Foreground = Fg, BorderBrush = Line, CaretBrush = Fg,
        Padding = new Thickness(4, 2, 4, 2), Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center,
    };

    CheckBox Check(string text, Func<bool> get, Action<bool> set)
    {
        var cb = new CheckBox { Content = T(text), IsChecked = get(), Foreground = Fg, Margin = new Thickness(0, 4, 0, 4) };
        cb.Checked += (_, _) => { set(true); Changed(); };
        cb.Unchecked += (_, _) => { set(false); Changed(); };
        return cb;
    }

    CheckBox RawCheck(string text, Action<bool> set)
    {
        var cb = new CheckBox { Content = T(text), Foreground = Fg, Margin = new Thickness(0, 4, 0, 4) };
        cb.Checked += (_, _) => set(true);
        cb.Unchecked += (_, _) => set(false);
        return cb;
    }

    UIElement SliderRow(string label, double min, double max, Func<double> get, Action<double> set, Func<double, string> fmt)
    {
        var val = Label(fmt(get()), 60);
        var s = new Slider { Minimum = min, Maximum = max, Value = get(), Width = 220, IsSnapToTickEnabled = true, TickFrequency = 1, VerticalAlignment = VerticalAlignment.Center };
        s.ValueChanged += (_, _) => { set(s.Value); val.Text = fmt(s.Value); Changed(); };
        return Row(Label(label, 210), s, val);
    }

    static Brush Checker()
    {
        // Szachownica + „gra” pod spodem, żeby było widać przezroczystość.
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing(Theme.B("#FF3C5A46"), null, new RectangleGeometry(new Rect(0, 0, 20, 20))));
        g.Children.Add(new GeometryDrawing(Theme.B("#FF4A6A52"), null, new RectangleGeometry(new Rect(0, 0, 10, 10))));
        g.Children.Add(new GeometryDrawing(Theme.B("#FF4A6A52"), null, new RectangleGeometry(new Rect(10, 10, 10, 10))));
        return new DrawingBrush(g) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 20, 20), ViewportUnits = BrushMappingMode.Absolute };
    }

    void Info(string text) => MessageBox.Show(this, T(text), "TS6 Overlay");
    bool Confirm(string text) => MessageBox.Show(this, T(text), "TS6 Overlay", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    /// <summary>Ciemne style kontrolek WPF (karty, przyciski, listy rozwijane).</summary>
    ResourceDictionary Styles()
    {
        var d = new ResourceDictionary();

        var tab = new Style(typeof(TabItem));
        tab.Setters.Add(new Setter(TemplateProperty, TabTemplate()));
        d.Add(typeof(TabItem), tab);

        var tc = new Style(typeof(TabControl));
        tc.Setters.Add(new Setter(Control.BackgroundProperty, Card));
        tc.Setters.Add(new Setter(Control.BorderBrushProperty, Line));
        d.Add(typeof(TabControl), tc);

        var btn = new Style(typeof(Button));
        btn.Setters.Add(new Setter(Control.BackgroundProperty, InputBg));
        btn.Setters.Add(new Setter(Control.ForegroundProperty, Fg));
        btn.Setters.Add(new Setter(Control.BorderBrushProperty, Line));
        btn.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));
        d.Add(typeof(Button), btn);

        var tb = new Style(typeof(ToggleButton));
        tb.Setters.Add(new Setter(Control.BackgroundProperty, InputBg));
        tb.Setters.Add(new Setter(Control.ForegroundProperty, Fg));
        tb.Setters.Add(new Setter(Control.BorderBrushProperty, Line));
        d.Add(typeof(ToggleButton), tb);

        return d;
    }

    ControlTemplate TabTemplate()
    {
        var t = new ControlTemplate(typeof(TabItem));
        var border = new FrameworkElementFactory(typeof(Border), "B");
        border.SetValue(Border.PaddingProperty, new Thickness(14, 9, 14, 9));
        border.SetValue(Border.MarginProperty, new Thickness(0, 0, 0, 2));
        border.SetValue(Border.MinWidthProperty, 150.0);
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6, 0, 0, 6));
        border.SetValue(Border.BackgroundProperty, Bg);
        var cp = new FrameworkElementFactory(typeof(ContentPresenter), "C");
        cp.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        cp.SetValue(TextElement.ForegroundProperty, Dim);
        border.AppendChild(cp);
        t.VisualTree = border;
        var sel = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
        sel.Setters.Add(new Setter(Border.BackgroundProperty, Card, "B"));
        sel.Setters.Add(new Setter(TextElement.ForegroundProperty, Fg, "C"));
        t.Triggers.Add(sel);
        return t;
    }
}
