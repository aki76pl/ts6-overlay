using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace TS6Overlay;

/// <summary>Statystyki sesji: kto ile mówił, ile razy wchodził, historia zdarzeń.</summary>
public sealed class StatsWindow : Window
{
    readonly Stats _stats;
    readonly ListView _people = new();
    readonly ListBox _history = new();
    readonly TextBlock _summary = new() { Margin = new Thickness(0, 0, 0, 8) };
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    int _historyShown = -1;

    static readonly Brush Bg = Theme.B("#FF1B1E24"), Card = Theme.B("#FF242830"), Fg = Theme.B("#FFE6E8EB"), Dim = Theme.B("#FF9AA1AC");

    sealed record Row(string Nick, string Talk, string Share, int Joins, int Messages, bool Talking);

    public StatsWindow(Stats stats)
    {
        _stats = stats;
        Title = "TS6 Overlay — statystyki sesji";
        Width = 640; Height = 620; MinWidth = 480; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Bg; Foreground = Fg;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;

        var gv = new GridView();
        gv.Columns.Add(Col("Nick", nameof(Row.Nick), 200));
        gv.Columns.Add(Col("Czas mówienia", nameof(Row.Talk), 120));
        gv.Columns.Add(Col("Udział", nameof(Row.Share), 70));
        gv.Columns.Add(Col("Wejścia", nameof(Row.Joins), 70));
        gv.Columns.Add(Col("Wiadomości", nameof(Row.Messages), 90));
        _people.View = gv;
        _people.Background = Card; _people.Foreground = Fg; _people.BorderThickness = new Thickness(0);
        _history.Background = Card; _history.Foreground = Fg; _history.BorderThickness = new Thickness(0);
        _summary.Foreground = Dim;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(Btn("Eksportuj CSV…", Export));
        buttons.Children.Add(Btn("Wyczyść", () =>
        {
            if (MessageBox.Show(this, "Wyczyścić statystyki tej sesji?", "TS6 Overlay", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            { _stats.Clear(); _historyShown = -1; Refresh(); }
        }));

        var grid = new Grid { Margin = new Thickness(12) };
        foreach (var h in new[] { GridLength.Auto, new GridLength(3, GridUnitType.Star), GridLength.Auto, new GridLength(2, GridUnitType.Star), GridLength.Auto })
            grid.RowDefinitions.Add(new RowDefinition { Height = h });
        Add(grid, _summary, 0);
        Add(grid, _people, 1);
        Add(grid, new TextBlock { Text = "Historia", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4) }, 2);
        Add(grid, _history, 3);
        Add(grid, buttons, 4);
        Content = grid;

        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Closed += (_, _) => _timer.Stop();
        Refresh();
    }

    static void Add(Grid g, UIElement e, int row) { Grid.SetRow(e, row); g.Children.Add(e); }

    static GridViewColumn Col(string header, string path, double width) =>
        new() { Header = header, Width = width, DisplayMemberBinding = new System.Windows.Data.Binding(path) };

    static Button Btn(string text, Action click)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0) };
        b.Click += (_, _) => click();
        return b;
    }

    static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes:00} min" : t.TotalMinutes >= 1 ? $"{t.Minutes} min {t.Seconds:00} s" : $"{t.Seconds} s";

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
        _summary.Text = $"Sesja od {_stats.Started:HH:mm} ({Fmt(DateTime.Now - _stats.Started)}) · łączny czas rozmów: {Fmt(TimeSpan.FromSeconds(total))} · osób: {people.Count}";

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
        catch (Exception ex) { MessageBox.Show(this, "Nie udało się zapisać: " + ex.Message, "TS6 Overlay"); }
    }
}
