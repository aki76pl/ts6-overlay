using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using static TS6Overlay.L;

namespace TS6Overlay;

/// <summary>Statystyki: bieżąca sesja, historia (dni/tygodnie/miesiące z wykresami) i archiwum czatu.</summary>
public sealed class StatsWindow : Window
{
    readonly Stats _stats;
    readonly LongTermStats _long;
    readonly ListView _people = new();
    readonly ListBox _history = new();
    readonly TextBlock _summary = new() { Margin = new Thickness(0, 0, 0, 8) };
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    int _historyShown = -1;

    static readonly Brush Bg = Theme.B("#FF1B1E24"), Card = Theme.B("#FF242830"), Fg = Theme.B("#FFE6E8EB"),
        Dim = Theme.B("#FF9AA1AC"), Accent = Theme.B("#FF2FBF55"), Accent2 = Theme.B("#FF1F8F3A"), Line = Theme.B("#FF3A404B");

    sealed record Row(string Nick, string Talk, string Share, int Joins, int Messages, bool Talking);
    sealed record LongRow(string Nick, string Online, string Talk, int Joins, int Messages);

    public StatsWindow(Stats stats, LongTermStats longTerm)
    {
        _stats = stats;
        _long = longTerm;
        Title = T("TS6 Overlay — statystyki");
        Width = 900; Height = 720; MinWidth = 600; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Bg; Foreground = Fg;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;

        var tabs = new TabControl { Background = Card, BorderBrush = Line, Margin = new Thickness(8) };
        tabs.Items.Add(new TabItem { Header = T("Ta sesja"), Content = SessionTab() });
        tabs.Items.Add(new TabItem { Header = T("Historia gry"), Content = LongTab() });
        tabs.Items.Add(new TabItem { Header = T("Archiwum czatu"), Content = ArchiveTab() });
        tabs.SelectionChanged += (_, e) =>
        {
            if (e.Source != tabs) return;
            if (tabs.SelectedIndex == 1) { _long.Save(); ShowPeriod(_period); }
            if (tabs.SelectedIndex == 2) RunSearch();
        };
        Content = tabs;

        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Closed += (_, _) => _timer.Stop();
        Refresh();
    }

    // =====================================================================
    // Ta sesja
    // =====================================================================

    UIElement SessionTab()
    {
        var gv = new GridView();
        gv.Columns.Add(Col("Nick", nameof(Row.Nick), 220));
        gv.Columns.Add(Col("Czas mówienia", nameof(Row.Talk), 130));
        gv.Columns.Add(Col("Udział", nameof(Row.Share), 80));
        gv.Columns.Add(Col("Wejścia", nameof(Row.Joins), 80));
        gv.Columns.Add(Col("Wiadomości", nameof(Row.Messages), 100));
        _people.View = gv;
        Dark(_people);
        Dark(_history);
        _summary.Foreground = Dim;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(Btn("Eksportuj CSV…", Export));
        buttons.Children.Add(Btn("Wyczyść", () =>
        {
            if (MessageBox.Show(this, T("Wyczyścić statystyki tej sesji?"), "TS6 Overlay", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            { _stats.Clear(); _historyShown = -1; Refresh(); }
        }));

        var grid = new Grid { Margin = new Thickness(12) };
        foreach (var h in new[] { GridLength.Auto, new GridLength(3, GridUnitType.Star), GridLength.Auto, new GridLength(2, GridUnitType.Star), GridLength.Auto })
            grid.RowDefinitions.Add(new RowDefinition { Height = h });
        Add(grid, _summary, 0);
        Add(grid, _people, 1);
        Add(grid, new TextBlock { Text = T("Historia"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4) }, 2);
        Add(grid, _history, 3);
        Add(grid, buttons, 4);
        return grid;
    }

    void Refresh()
    {
        var people = _stats.People.Values.OrderByDescending(p => p.TotalTalk).ToList();
        double total = people.Sum(p => p.TotalTalk.TotalSeconds);
        int sel = _people.SelectedIndex;
        _people.ItemsSource = people.Select(p => new Row(
            (p.TalkingSince != null ? "🎙 " : "") + p.Nickname,
            Fmt(p.TotalTalk),
            total > 0 ? $"{p.TotalTalk.TotalSeconds / total:P0}" : "—",
            p.Joins, p.Messages, p.TalkingSince != null)).ToList();
        _people.SelectedIndex = sel;
        _summary.Text = T("Sesja od {0:HH:mm} ({1}) · łączny czas rozmów: {2} · osób: {3}", _stats.Started, Fmt(DateTime.Now - _stats.Started), Fmt(TimeSpan.FromSeconds(total)), people.Count);

        if (_historyShown != _stats.History.Count)
        {
            _historyShown = _stats.History.Count;
            _history.ItemsSource = _stats.History.AsEnumerable().Reverse().Select(h => $"{h.Time:HH:mm:ss}   {h.Text}").ToList();
        }
    }

    void Export()
    {
        var d = new Microsoft.Win32.SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = $"ts6-statystyki-{DateTime.Now:yyyy-MM-dd-HHmm}.csv" };
        if (d.ShowDialog(this) != true) return;
        try { _stats.ExportCsv(d.FileName); }
        catch (Exception ex) { MessageBox.Show(this, T("Nie udało się zapisać: {0}", ex.Message), "TS6 Overlay"); }
    }

    // =====================================================================
    // Historia gry (długoterminowa)
    // =====================================================================

    int _period = 7;
    readonly StackPanel _periodButtons = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
    readonly TextBlock _longSummary = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    readonly StackPanel _dayChart = new() { Orientation = Orientation.Horizontal, Height = 150, VerticalAlignment = VerticalAlignment.Bottom };
    readonly StackPanel _topChart = new();
    readonly ListView _longPeople = new();

    UIElement LongTab()
    {
        foreach (var (days, label) in new[] { (1, "Dziś"), (7, "7 dni"), (30, "30 dni"), (90, "3 miesiące"), (0, "Wszystko") })
        {
            var b = Btn(label, () => ShowPeriod(days));
            b.Tag = days;
            _periodButtons.Children.Add(b);
        }
        var gv = new GridView();
        gv.Columns.Add(Col("Nick", nameof(LongRow.Nick), 220));
        gv.Columns.Add(Col("Na serwerze", nameof(LongRow.Online), 130));
        gv.Columns.Add(Col("Czas mówienia", nameof(LongRow.Talk), 130));
        gv.Columns.Add(Col("Wejścia", nameof(LongRow.Joins), 80));
        gv.Columns.Add(Col("Wiadomości", nameof(LongRow.Messages), 100));
        _longPeople.View = gv;
        Dark(_longPeople);
        _longSummary.Foreground = Dim;

        var p = new StackPanel { Margin = new Thickness(12) };
        p.Children.Add(_periodButtons);
        p.Children.Add(_longSummary);
        p.Children.Add(Caption("Twój czas na TeamSpeaku (dziennie)"));
        p.Children.Add(new Border
        {
            Background = Bg, BorderBrush = Line, BorderThickness = new Thickness(1), Padding = new Thickness(8), Height = 190,
            Child = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _dayChart },
        });
        p.Children.Add(Caption("Najdłużej na serwerze"));
        p.Children.Add(new Border { Background = Bg, BorderBrush = Line, BorderThickness = new Thickness(1), Padding = new Thickness(8), Child = _topChart });
        p.Children.Add(Caption("Wszyscy"));
        _longPeople.MaxHeight = 320;
        p.Children.Add(_longPeople);
        p.Children.Add(new TextBlock
        {
            Text = T("Liczone tylko wtedy, gdy TS6 Overlay działa: czas obecności co minutę, czas mówienia na bieżąco."),
            Foreground = Dim, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0),
        });
        return new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    void ShowPeriod(int days)
    {
        _period = days;
        foreach (Button b in _periodButtons.Children)
        {
            bool on = (int)b.Tag == days;
            b.Background = on ? Accent : Card;
            b.Foreground = on ? Brushes.Black : Fg;
        }
        var to = DateTime.Today;
        var from = days == 0 ? _long.FirstDay() : to.AddDays(-(days - 1));
        if (from > to) from = to;
        var dayList = _long.Days(from, to);
        var totals = _long.Totals(from, to);
        double myMin = dayList.Sum(d => d.stats.MyOnlineMin);
        double talkSec = totals.Sum(t => t.TalkSec);
        _longSummary.Text = T("Od {0:dd.MM.yyyy}: Twój czas na TS: {1} · łączny czas rozmów: {2} · osób: {3} · aktywnych dni: {4}",
            from, Hours(myMin), Fmt(TimeSpan.FromSeconds(talkSec)), totals.Count, dayList.Count(d => d.stats.MyOnlineMin > 0));

        // wykres dzienny
        _dayChart.Children.Clear();
        double max = Math.Max(30, dayList.Max(d => d.stats.MyOnlineMin));
        int every = dayList.Count > 60 ? 7 : dayList.Count > 20 ? 3 : 1;
        for (int i = 0; i < dayList.Count; i++)
        {
            var (date, s) = dayList[i];
            double h = s.MyOnlineMin / max * 130;
            var col = new StackPanel { Width = dayList.Count > 40 ? 10 : 26, Margin = new Thickness(1, 0, 1, 0), VerticalAlignment = VerticalAlignment.Bottom };
            col.Children.Add(new Border
            {
                Height = Math.Max(s.MyOnlineMin > 0 ? 2 : 0, h), Background = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? Accent2 : Accent,
                VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(2, 2, 0, 0),
                ToolTip = $"{date:ddd dd.MM}: {Hours(s.MyOnlineMin)}",
            });
            // Podpis może być szerszy niż słupek — Canvas go nie przycina.
            var label = new Canvas { Height = 16, ClipToBounds = false };
            if (i % every == 0)
            {
                var tb = new TextBlock { Text = date.ToString(dayList.Count > 20 ? "dd.MM" : "dd"), FontSize = 10, Foreground = Dim };
                Canvas.SetLeft(tb, dayList.Count > 40 ? -2 : 4);
                Canvas.SetTop(tb, 2);
                label.Children.Add(tb);
            }
            col.Children.Add(label);
            _dayChart.Children.Add(col);
        }

        // top 10 obecności
        _topChart.Children.Clear();
        var top = totals.OrderByDescending(t => t.OnlineMin).Take(10).ToList();
        double topMax = Math.Max(1, top.Select(t => t.OnlineMin).DefaultIfEmpty(1).Max());
        if (top.Count == 0) _topChart.Children.Add(new TextBlock { Text = T("Brak danych z tego okresu."), Foreground = Dim });
        foreach (var t in top)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            var name = new TextBlock { Text = t.Nick, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            var bar = new Border
            {
                Height = 14, Background = Accent, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(0, 2, 2, 0),
                ToolTip = T("mówił(a): {0}", Fmt(TimeSpan.FromSeconds(t.TalkSec))),
            };
            var barHost = new Grid();
            barHost.Children.Add(bar);
            barHost.SizeChanged += (_, _) => bar.Width = Math.Max(2, barHost.ActualWidth * t.OnlineMin / topMax);
            var val = new TextBlock { Text = Hours(t.OnlineMin), Foreground = Dim, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(barHost, 1);
            Grid.SetColumn(val, 2);
            row.Children.Add(name); row.Children.Add(barHost); row.Children.Add(val);
            _topChart.Children.Add(row);
        }

        _longPeople.ItemsSource = totals.OrderByDescending(t => t.OnlineMin)
            .Select(t => new LongRow(t.Nick, Hours(t.OnlineMin), Fmt(TimeSpan.FromSeconds(t.TalkSec)), t.Joins, t.Messages)).ToList();
    }

    static string Hours(double minutes) => minutes >= 60 ? $"{(int)(minutes / 60)} h {(int)(minutes % 60):00} min" : $"{(int)minutes} min";

    // =====================================================================
    // Archiwum czatu
    // =====================================================================

    readonly TextBox _query = new() { MinWidth = 260, Padding = new Thickness(4, 2, 4, 2), Margin = new Thickness(0, 0, 8, 0) };
    readonly ComboBox _range = new() { MinWidth = 140, Margin = new Thickness(0, 0, 8, 0) };
    readonly ListBox _results = new();
    readonly TextBlock _resultInfo = new() { Margin = new Thickness(0, 6, 0, 0) };

    UIElement ArchiveTab()
    {
        _query.Background = Card; _query.Foreground = Fg; _query.CaretBrush = Fg; _query.BorderBrush = Line;
        _query.KeyUp += (_, e) => { if (e.Key == Key.Enter) RunSearch(); };
        _query.TextChanged += (_, _) => RunSearch();
        foreach (var r in new[] { "7 dni", "30 dni", "Rok", "Wszystko" }) _range.Items.Add(T(r));
        _range.SelectedIndex = 1;
        _range.SelectionChanged += (_, _) => RunSearch();
        Dark(_results);
        _resultInfo.Foreground = Dim;

        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        bar.Children.Add(new TextBlock { Text = T("Szukaj:"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        bar.Children.Add(_query);
        bar.Children.Add(_range);
        bar.Children.Add(Btn("Otwórz folder archiwum", () =>
        {
            System.IO.Directory.CreateDirectory(ChatArchive.Dir);
            Process.Start(new ProcessStartInfo("explorer.exe", ChatArchive.Dir));
        }));

        var grid = new Grid { Margin = new Thickness(12) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Add(grid, bar, 0);
        Add(grid, _results, 1);
        Add(grid, _resultInfo, 2);
        return grid;
    }

    void RunSearch()
    {
        var from = _range.SelectedIndex switch
        {
            0 => DateTime.Today.AddDays(-6),
            1 => DateTime.Today.AddDays(-29),
            2 => DateTime.Today.AddYears(-1),
            _ => DateTime.MinValue,
        };
        var found = ChatArchive.Search(_query.Text.Trim(), from);
        _results.ItemsSource = found.Select(e =>
            $"{e.Time:yyyy-MM-dd HH:mm}  {(e.Kind == nameof(NoticeKind.Poke) ? "👉 " : "✉ ")}{e.Text}" + (e.Server != "" ? $"   [{e.Server}]" : "")).ToList();
        _resultInfo.Text = found.Count == 0
            ? T("Nic nie znaleziono. Archiwum zapisuje wiadomości i szturchnięcia od wersji 3.1.")
            : T("Znaleziono: {0}", found.Count) + (found.Count >= 1000 ? " " + T("(pokazano 1000 najnowszych)") : "");
    }

    // =====================================================================
    // Pomocnicze
    // =====================================================================

    static void Dark(ItemsControl c) { c.Background = Card; c.Foreground = Fg; c.BorderThickness = new Thickness(0); }

    TextBlock Caption(string text) => new() { Text = T(text), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 6) };

    static void Add(Grid g, UIElement e, int row) { Grid.SetRow(e, row); g.Children.Add(e); }

    static GridViewColumn Col(string header, string path, double width) =>
        new() { Header = T(header), Width = width, DisplayMemberBinding = new System.Windows.Data.Binding(path) };

    static Button Btn(string text, Action click)
    {
        var b = new Button { Content = T(text), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0) };
        b.Click += (_, _) => click();
        return b;
    }

    static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes:00} min" : t.TotalMinutes >= 1 ? $"{t.Minutes} min {t.Seconds:00} s" : $"{t.Seconds} s";
}
