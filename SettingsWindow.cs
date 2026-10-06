using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace TS6Overlay;

/// <summary>Okno ustawień: wszystkie opcje z podglądem nakładki na żywo. Zmiany zapisują się od razu.</summary>
public sealed class SettingsWindow : Window
{
    public const int TabLook = 0, TabEditor = 1, TabBehavior = 2, TabNotify = 3, TabBinds = 4, TabPeople = 5, TabObs = 6, TabUpdates = 7;

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
        Title = $"TS6 Overlay {Updater.Current} — ustawienia";
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
        _tabs.Items.Add(Tab("Aktualizacje", UpdatesTab()));
        _tabs.SelectionChanged += (_, e) =>
        {
            if (e.Source != _tabs) return;
            if (_tabs.SelectedIndex != TabEditor) { _app.PreviewTheme(null); ShowTheme(Theme.ByName(_cfg.Theme)); }
            else ShowTheme(_edit);
            if (_tabs.SelectedIndex == TabPeople) RebuildPeople();
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
                if (!Confirm($"Usunąć motyw „{t.Name}”?")) return;
                Theme.DeleteCustom(t.Name);
                _cfg.Theme = "Terminal";
                FillThemeCombo();
                Changed();
            })));
        p.Children.Add(Hint("Motywy od znajomych to pliki *.ts6theme — wczytasz je przyciskiem „Importuj…”."));

        p.Children.Add(Header("Rozmiar i przezroczystość"));
        p.Children.Add(SliderRow("Rozmiar", 60, 200, () => _cfg.Scale * 100, v => _cfg.Scale = v / 100, v => $"{v:0}%"));
        p.Children.Add(SliderRow("Przezroczystość", 30, 100, () => _cfg.Opacity * 100, v => _cfg.Opacity = v / 100, v => $"{v:0}%"));

        p.Children.Add(Header("Zawartość"));
        p.Children.Add(Check("Pokazuj wszystkich na kanale (odznacz = tylko mówiący i powiadomienia)", () => _cfg.ShowChannelList, v => _cfg.ShowChannelList = v));
        p.Children.Add(SliderRow("Czas powiadomień wejścia/wyjścia", 2, 30, () => _cfg.EventSeconds, v => _cfg.EventSeconds = (int)v, v => $"{v:0} s"));
        p.Children.Add(SliderRow("Czas wiadomości i szturchnięć", 3, 60, () => _cfg.MessageSeconds, v => _cfg.MessageSeconds = (int)v, v => $"{v:0} s"));

        p.Children.Add(Header("Położenie"));
        p.Children.Add(Hint("Przytrzymaj Ctrl i przeciągnij nakładkę lewym przyciskiem myszy. Ctrl+Shift+O ukrywa/pokazuje nakładkę."));
        p.Children.Add(Row(Btn("Przywróć pozycję (lewy górny róg)", () => { _cfg.Left = 20; _cfg.Top = 200; RestartOverlayPosition(); })));
        return Scroll(p);
    }

    void RestartOverlayPosition()
    {
        foreach (Window w in Application.Current.Windows)
            if (w is OverlayWindow o) { o.Left = _cfg.Left; o.Top = _cfg.Top; }
        Changed();
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
        var d = new Microsoft.Win32.OpenFileDialog { Filter = "Motyw TS6 Overlay (*.ts6theme)|*.ts6theme|JSON (*.json)|*.json", Title = "Importuj motyw" };
        if (d.ShowDialog(this) != true) return;
        try
        {
            var t = Theme.Import(d.FileName);
            _cfg.Theme = t.Name;
            FillThemeCombo();
            Changed();
            Info($"Zaimportowano motyw „{t.Name}”.");
        }
        catch (Exception ex) { Info("Nie udało się wczytać motywu: " + ex.Message); }
    }

    void ExportTheme(Theme t)
    {
        var d = new Microsoft.Win32.SaveFileDialog { Filter = "Motyw TS6 Overlay (*.ts6theme)|*.ts6theme", FileName = t.Name + Theme.Extension, Title = "Eksportuj motyw" };
        if (d.ShowDialog(this) != true) return;
        try { t.Export(d.FileName); Info("Zapisano. Wyślij ten plik znajomemu — wczyta go przyciskiem „Importuj…”."); }
        catch (Exception ex) { Info("Nie udało się zapisać: " + ex.Message); }
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
        _nameBox.Text = copy ? UniqueName(t.Name + " (mój)") : t.Name;
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
        catch (Exception ex) { Info("Nie udało się zapisać: " + ex.Message); return; }
        _cfg.Theme = name;
        _app.PreviewTheme(null);
        FillThemeCombo();
        _loadingEditor = true;
        _baseCombo!.SelectedItem = name;
        _loadingEditor = false;
        _edit = t;
        Changed();
        Info($"Zapisano motyw „{name}” i ustawiono go na nakładce.");
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
        var fade = new RadioButton { Content = "Nakładka blednie", IsChecked = _cfg.AutoHideMode != "Header", Foreground = Fg, Margin = new Thickness(0, 4, 16, 4) };
        var header = new RadioButton { Content = "Zostaje sam nagłówek z nazwą kanału", IsChecked = _cfg.AutoHideMode == "Header", Foreground = Fg, Margin = new Thickness(0, 4, 0, 4) };
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

        p.Children.Add(Header("Diagnostyka"));
        p.Children.Add(Check("Zapisuj surowe zdarzenia TeamSpeak (events.log)", () => _cfg.LogRawEvents, v => _cfg.LogRawEvents = v));
        p.Children.Add(Row(Btn("Otwórz folder ustawień", () => Process.Start(new ProcessStartInfo("explorer.exe", Config.Dir)))));
        return Scroll(p);
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
        p.Children.Add(Hint("Każdemu zdarzeniu możesz przypisać własny dźwięk: kliknij „Wybierz…” albo przeciągnij plik (.wav, .mp3, .wma, .aiff, .m4a) na wiersz. " +
                            $"Program kopiuje plik do swojego folderu, a odtwarza najwyżej {Sounds.MaxSeconds:0} s."));
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

        p.Children.Add(Header("Mikrofon wyciszony"));
        p.Children.Add(Check("Pokazuj czerwony pasek, gdy mikrofon lub głośniki są wyciszone", () => _cfg.MuteWarning, v => _cfg.MuteWarning = v));
        p.Children.Add(Check("Ostrzegaj, gdy mówisz do wyciszonego mikrofonu (pasek miga)", () => _cfg.MuteVoiceDetect, v => _cfg.MuteVoiceDetect = v));
        p.Children.Add(SliderRow("Czułość (niżej = czulej)", 1, 60, () => _cfg.MuteSensitivity, v => _cfg.MuteSensitivity = (int)v, v => $"{v:0}"));

        _micLevel = new ProgressBar { Width = 300, Height = 14, Minimum = 0, Maximum = 100, Foreground = AccentB, Background = InputBg, BorderBrush = Line };
        _micThreshold = new Rectangle { Width = 2, Height = 18, Fill = Brushes.IndianRed, HorizontalAlignment = HorizontalAlignment.Left };
        var meter = new Grid { Width = 300 };
        meter.Children.Add(_micLevel);
        meter.Children.Add(_micThreshold);
        var testBtn = new ToggleButton { Content = "Test mikrofonu", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(10, 0, 0, 0) };
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
            file.Text = f == null ? "dźwięk wbudowany" : $"♪ {System.IO.Path.GetFileName(f)} ({Sounds.Length(f).TotalSeconds:0.#} s)";
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
                    Info($"Plik trwa {len.TotalSeconds:0} s — przy powiadomieniu zagra tylko pierwsze {Sounds.MaxSeconds:0} s.");
            }
            catch (Exception ex) { Info("Nie udało się wczytać dźwięku: " + ex.Message); }
        }

        var buttons = Row(
            Btn("▶", () => Sounds.Play(kind)),
            Btn("Wybierz…", () =>
            {
                var d = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Dźwięk: " + label,
                    Filter = "Dźwięki (*.wav;*.mp3;*.wma;*.aiff;*.m4a)|*.wav;*.mp3;*.wma;*.aiff;*.m4a",
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
        }, primary: true), Btn("■ Zatrzymaj odtwarzanie", () => _app.Binds.Stop())));
        _bindList = new StackPanel();
        p.Children.Add(_bindList);

        p.Children.Add(Header("Gdzie grać dźwięki bindów"));
        var dev = new ComboBox { MinWidth = 300 };
        var devices = BindManager.OutputDevices();
        dev.Items.Add("Domyślne urządzenie (słyszysz tylko Ty)");
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

    void RebuildBinds()
    {
        if (_bindList == null) return;
        _bindList.Children.Clear();
        if (_cfg.Binds.Count == 0) _bindList.Children.Add(Hint("Nie masz jeszcze żadnych bindów — kliknij „+ Dodaj bind”."));
        foreach (var b in _cfg.Binds.ToList()) _bindList.Children.Add(BindRow(b));
    }

    UIElement BindRow(SoundBind b)
    {
        var enabled = new CheckBox { IsChecked = b.Enabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Włączony" };
        enabled.Checked += (_, _) => { b.Enabled = true; Changed(); RebuildBinds(); };
        enabled.Unchecked += (_, _) => { b.Enabled = false; Changed(); RebuildBinds(); };

        var name = Input(170);
        name.Text = b.Name;
        name.LostFocus += (_, _) => { var n = name.Text.Trim(); if (n != "" && n != b.Name) { b.Name = n; Changed(); } };

        var keyBtn = Btn(b.KeyText, () => { });
        keyBtn.MinWidth = 120;
        keyBtn.ToolTip = "Kliknij i naciśnij klawisz lub skrót";
        keyBtn.Click += (_, _) => StartCapture(b, keyBtn);

        var line1 = Row(enabled, name, keyBtn,
            Btn("▶", () => _app.Binds.Trigger(b)),
            Btn("Usuń", () =>
            {
                if (!Confirm($"Usunąć bind „{b.Name}”?")) return;
                _cfg.Binds.Remove(b);
                Sounds.TryDelete(b.SoundPath);
                Changed();
                RebuildBinds();
            }));

        var file = new TextBlock { MaxWidth = 230, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        bool hasFile = b.SoundPath != "" && System.IO.File.Exists(b.SoundPath);
        file.Text = hasFile ? $"♪ {System.IO.Path.GetFileName(b.SoundPath)} ({Sounds.Length(b.SoundPath).TotalSeconds:0.#} s)" : "brak dźwięku";
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
            catch (Exception ex) { Info("Nie udało się wczytać dźwięku: " + ex.Message); }
        }

        var vol = new Slider { Minimum = 0, Maximum = 100, Value = b.Volume, Width = 110, VerticalAlignment = VerticalAlignment.Center, IsSnapToTickEnabled = true, TickFrequency = 5 };
        var volText = Label($"{b.Volume}%", 44, dim: true);
        vol.ValueChanged += (_, _) => { b.Volume = (int)vol.Value; volText.Text = $"{b.Volume}%"; _app.Cfg.Save(); };

        var line2 = Row(
            Btn("Wybierz dźwięk…", () =>
            {
                var d = new Microsoft.Win32.OpenFileDialog { Title = "Dźwięk binda: " + b.Name, Filter = "Dźwięki (*.wav;*.mp3;*.wma;*.aiff;*.m4a)|*.wav;*.mp3;*.wma;*.aiff;*.m4a" };
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
                Text = $"Skrót {b.KeyText} jest zajęty przez inny program — wybierz inny.",
                Foreground = Brushes.IndianRed, Margin = new Thickness(26, 2, 0, 0), FontSize = 12,
            });
        else if (_cfg.Binds.Any(o => o != b && o.Enabled && b.Enabled && o.Key == b.Key && o.Modifiers == b.Modifiers && b.Key != 0))
            box.Children.Add(new TextBlock
            {
                Text = $"Ten sam skrót ma inny bind.", Foreground = Brushes.IndianRed, Margin = new Thickness(26, 2, 0, 0), FontSize = 12,
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
        btn.Content = "Naciśnij skrót…  (Esc = anuluj, Backspace = usuń)";
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

        p.Children.Add(Header("Ignorowani"));
        _ignList = new StackPanel();
        p.Children.Add(Card_(_ignList));
        RebuildPeople();
        return Scroll(p);
    }

    void RebuildPeople()
    {
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
            string soundLabel = f.SoundPath == "" ? "Dźwięk: wbudowany" : "Dźwięk: " + System.IO.Path.GetFileName(f.SoundPath);
            _favList.Children.Add(Row(Label("★ " + f.Nickname, 200), swatch,
                Btn(soundLabel, () =>
                {
                    var d = new Microsoft.Win32.OpenFileDialog { Filter = "Dźwięki (*.wav;*.mp3;*.wma;*.aiff;*.m4a)|*.wav;*.mp3;*.wma;*.aiff;*.m4a", Title = $"Dźwięk wejścia: {f.Nickname}" };
                    if (d.ShowDialog(this) != true) return;
                    try
                    {
                        var key = "fav-" + string.Concat(f.Uid.Where(char.IsLetterOrDigit).Take(16));
                        var (path, _) = Sounds.Import(d.FileName, key);
                        f.SoundPath = path;
                        RebuildPeople(); Changed();
                        Sounds.Play(SoundKind.Favorite, path);
                    }
                    catch (Exception ex) { Info("Nie udało się wczytać dźwięku: " + ex.Message); }
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
    // Aktualizacje i instalacja
    // =====================================================================

    TextBlock _updStatus = null!, _instStatus = null!;

    UIElement UpdatesTab()
    {
        var p = Page();
        p.Children.Add(Header("Wersja"));
        p.Children.Add(Label($"Wersja programu: {Updater.Current}"));
        p.Children.Add(Check("Sprawdzaj aktualizacje przy starcie", () => _cfg.AutoUpdateCheck, v => _cfg.AutoUpdateCheck = v));
        _updStatus = Hint("");
        p.Children.Add(Row(Btn("Sprawdź teraz", CheckNow, primary: true),
            Btn("Strona wydań", () => Process.Start(new ProcessStartInfo(Updater.ReleasesPage) { UseShellExecute = true }))));
        p.Children.Add(_updStatus);

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
        _instStatus.Text = Installer.IsInstalledCopy ? $"Zainstalowany w {Installer.InstallDir}."
            : Installer.IsInstalled ? $"Zainstalowana kopia jest w {Installer.InstallDir}, ale uruchomiona jest ta: {Environment.ProcessPath}"
            : $"Nie zainstalowano — program działa z: {Environment.ProcessPath}";
    }

    async void CheckNow()
    {
        _updStatus.Text = "Sprawdzam…";
        try
        {
            var r = await Updater.CheckAsync();
            if (r == null) { _updStatus.Text = $"Masz najnowszą wersję ({Updater.Current})."; return; }
            _updStatus.Text = $"Dostępna wersja {r.Version}.";
            await _app.AskAndUpdate(r);
        }
        catch (Exception ex) { _updStatus.Text = "Nie udało się sprawdzić: " + ex.Message; }
    }

    // =====================================================================
    // Odświeżanie (poziom mikrofonu, status OBS)
    // =====================================================================

    void TimerTick()
    {
        if (_micLevel != null)
        {
            _micLevel.Value = Math.Min(100, _app.Mic.Level * 100);
            _micThreshold.Margin = new Thickness(Math.Clamp(_cfg.MuteSensitivity, 1, 100) / 100.0 * 300, 0, 0, 0);
        }
        if (_obsUrl != null)
        {
            _obsUrl.Text = $"http://localhost:{_cfg.ObsPort}/";
            _obsStatus.Text = !_cfg.ObsEnabled ? "Wyłączone."
                : _app.ObsRunning ? "Działa — wklej adres do OBS."
                : "Nie udało się uruchomić: " + (_app.Obs.Error ?? "port zajęty?") + " Spróbuj innego portu.";
        }
    }

    // =====================================================================
    // Budowanie interfejsu
    // =====================================================================

    static TabItem Tab(string header, UIElement content) => new() { Header = header, Content = content };

    static StackPanel Page() => new() { Margin = new Thickness(16, 8, 16, 16) };

    ScrollViewer Scroll(UIElement e) => new() { Content = e, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Card };

    TextBlock Header(string text) => new()
    {
        Text = text, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = AccentB, Margin = new Thickness(0, 16, 0, 6),
    };

    TextBlock Label(string text, double width = double.NaN, bool dim = false) => new()
    {
        Text = text, Width = width, Foreground = dim ? Dim : Fg, VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis,
    };

    TextBlock Hint(string text) => new()
    {
        Text = text, Foreground = Dim, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4),
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
        var b = new Button { Content = text, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0), MinHeight = 26 };
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
        var cb = new CheckBox { Content = text, IsChecked = get(), Foreground = Fg, Margin = new Thickness(0, 4, 0, 4) };
        cb.Checked += (_, _) => { set(true); Changed(); };
        cb.Unchecked += (_, _) => { set(false); Changed(); };
        return cb;
    }

    CheckBox RawCheck(string text, Action<bool> set)
    {
        var cb = new CheckBox { Content = text, Foreground = Fg, Margin = new Thickness(0, 4, 0, 4) };
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

    void Info(string text) => MessageBox.Show(this, text, "TS6 Overlay");
    bool Confirm(string text) => MessageBox.Show(this, text, "TS6 Overlay", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

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
