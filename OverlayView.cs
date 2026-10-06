using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace TS6Overlay;

/// <summary>
/// Zawartość nakładki: pasek „mikrofon wyciszony”, nazwa kanału, lista osób, powiadomienia.
/// Używana w oknie nakładki i w podglądzie w ustawieniach.
/// </summary>
public sealed class OverlayView : StackPanel
{
    readonly Config _cfg;
    readonly bool _preview;
    readonly Border _banner = new() { Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 0, 4), Visibility = Visibility.Collapsed };
    readonly TextBlock _bannerIcon = new() { FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 18, Text = "", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Foreground = Brushes.White };
    readonly TextBlock _bannerText = new() { FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
    readonly Border _panel = new() { Padding = new Thickness(10, 8, 10, 8) };
    readonly TextBlock _channel = new() { FontWeight = FontWeights.SemiBold, FontSize = 12, Margin = new Thickness(0, 0, 0, 4) };
    readonly StackPanel _members = new();
    readonly StackPanel _notices = new();
    readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    Theme _t = Theme.ByName("Terminal");
    OverlayState _state = OverlayState.Disconnected;
    bool _highlight, _voiceWhileMuted, _idle;
    string? _hint;
    DateTime _lastActivity = DateTime.Now;

    public OverlayView(Config cfg, bool preview = false)
    {
        _cfg = cfg;
        _preview = preview;
        MinWidth = 190;

        var bannerRow = new StackPanel { Orientation = Orientation.Horizontal };
        bannerRow.Children.Add(_bannerIcon);
        bannerRow.Children.Add(_bannerText);
        _banner.Child = bannerRow;

        var inner = new StackPanel();
        inner.Children.Add(_channel);
        inner.Children.Add(_members);
        inner.Children.Add(_notices);
        _panel.Child = inner;

        Children.Add(_banner);
        Children.Add(_panel);
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        _idleTimer.Tick += (_, _) => CheckIdle();
        if (!preview) _idleTimer.Start();
    }

    public Theme Theme => _t;

    public void ApplyTheme(Theme t)
    {
        _t = t;
        TextElement.SetFontFamily(this, new FontFamily(t.Font));
        _panel.CornerRadius = new CornerRadius(t.Radius);
        _banner.CornerRadius = new CornerRadius(Math.Min(t.Radius, 6));
        _banner.Background = Theme.B(t.Banner);
        _channel.Foreground = Theme.B(t.Header);
        _channel.Effect = TextShadow();
        ApplyBorder();
        Render(_state);
    }

    /// <summary>Podświetlenie ramki (przesuwanie Ctrl+mysz / tryb edycji) i opcjonalna podpowiedź.</summary>
    public void SetHighlight(bool on, string? hint = null)
    {
        _highlight = on;
        _hint = on ? hint : null;
        ApplyBorder();
        Render(_state);
        Touch();
    }

    void ApplyBorder()
    {
        _panel.BorderBrush = _highlight ? Theme.B(_t.Accent) : Theme.B(_t.PanelBorder);
        _panel.BorderThickness = new Thickness(_highlight ? 2 : _t.BorderWidth);
    }

    DropShadowEffect? TextShadow() => _t.TextShadow
        ? new() { Color = Colors.Black, BlurRadius = 4, ShadowDepth = 1, Opacity = 0.9 }
        : null;

    public void Render(OverlayState s)
    {
        _state = s;
        _channel.Text = _hint ?? (s.Channel == null ? "TeamSpeak: brak połączenia" : ("🔊 " + s.Channel).TrimEnd());
        _members.Children.Clear();
        bool anyTalking = false;
        foreach (var m in s.Members)
        {
            anyTalking |= m.Talking;
            if (!_cfg.ShowChannelList && !m.Talking) continue;
            _members.Children.Add(Row(m, m.Id == s.MyId));
        }
        if (anyTalking) Touch();
        UpdateBanner();
        UpdatePanelBackground();
    }

    UIElement Row(ClientInfo m, bool isMe)
    {
        var fav = _cfg.FavoriteFor(m.Uid);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        string dotColor = m.Talking ? (m.Whisper ? _t.Whisper : _t.Talk) : _t.Idle;
        Shape dot = _t.SquareDots ? new Rectangle() : new Ellipse();
        dot.Width = 10; dot.Height = 10;
        dot.Margin = new Thickness(0, 0, 7, 0);
        dot.VerticalAlignment = VerticalAlignment.Center;
        dot.Fill = Theme.B(dotColor);
        if (m.Talking && _t.Glow) dot.Effect = new DropShadowEffect { Color = Theme.C(dotColor), BlurRadius = 9, ShadowDepth = 0 };
        row.Children.Add(dot);
        row.Children.Add(new TextBlock
        {
            Text = (fav != null ? "★ " : "") + m.Nickname + (isMe ? " (ty)" : ""),
            FontSize = 14,
            FontWeight = m.Talking ? FontWeights.Bold : FontWeights.Normal,
            Foreground = fav != null ? SafeBrush(fav.Color, _t.TextTalking) : Theme.B(m.Talking ? _t.TextTalking : _t.Text),
            VerticalAlignment = VerticalAlignment.Center,
            Effect = TextShadow(),
        });
        // Ikony Segoe MDL2 Assets: wyciszony mikrofon / wyciszone głośniki.
        if (m.InputMuted) row.Children.Add(MuteIcon(""));
        if (m.OutputMuted) row.Children.Add(MuteIcon(""));
        return new Border
        {
            Child = row,
            Margin = new Thickness(-4, 0, -4, 0),
            Padding = new Thickness(4, 1, 4, 1),
            CornerRadius = new CornerRadius(Math.Min(_t.Radius, 4)),
            Background = m.Talking ? Theme.B(_t.TalkRow) : Brushes.Transparent,
        };
    }

    static Brush SafeBrush(string hex, string fallback)
    {
        try { return Theme.B(hex); } catch { return Theme.B(fallback); }
    }

    TextBlock MuteIcon(string glyph) => new()
    {
        Text = glyph,
        FontFamily = new FontFamily("Segoe MDL2 Assets"),
        FontSize = 12,
        Margin = new Thickness(6, 1, 0, 0),
        Foreground = Theme.B(_t.Muted),
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Dodaje powiadomienie; persistent = nie znika (podgląd w ustawieniach).</summary>
    public void AddNotice(Notice n, bool persistent = false)
    {
        var (bg, prefix) = n.Kind switch
        {
            NoticeKind.Join => (_t.Join, "➜ "),
            NoticeKind.Leave => (_t.Leave, "← "),
            NoticeKind.Message => (_t.Message, "✉ "),
            NoticeKind.Poke => (_t.Poke, "👉 "),
            _ => (_t.Info, "• "),
        };
        var b = new Border
        {
            CornerRadius = new CornerRadius(Math.Min(_t.Radius, 5)),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 4, 0, 0),
            MaxWidth = 420,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Theme.B(bg),
            Child = new TextBlock
            {
                Text = prefix + n.Text,
                Foreground = Theme.B(_t.NoticeText),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                FontWeight = n.Kind == NoticeKind.Poke ? FontWeights.Bold : FontWeights.Normal,
            },
        };
        _notices.Children.Add(b);
        while (_notices.Children.Count > 6) _notices.Children.RemoveAt(0);
        Touch();
        UpdatePanelBackground();
        if (persistent) return;

        int secs = n.Kind is NoticeKind.Message or NoticeKind.Poke ? _cfg.MessageSeconds : _cfg.EventSeconds;
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(600)) { BeginTime = TimeSpan.FromSeconds(Math.Max(1, secs)) };
        fade.Completed += (_, _) => { _notices.Children.Remove(b); UpdatePanelBackground(); };
        b.BeginAnimation(OpacityProperty, fade);

        if (n.Kind == NoticeKind.Poke)
        {
            var shake = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(420) };
            foreach (var (ms, x) in new[] { (0, 0.0), (60, -6.0), (120, 6.0), (180, -5.0), (240, 5.0), (300, -2.0), (420, 0.0) })
                shake.KeyFrames.Add(new LinearDoubleKeyFrame(x, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
            var tr = new TranslateTransform();
            b.RenderTransform = tr;
            tr.BeginAnimation(TranslateTransform.XProperty, shake);
        }
    }

    public void ClearNotices() => _notices.Children.Clear();

    // ---------- ostrzeżenie: mikrofon wyciszony ----------

    public void SetVoiceWhileMuted(bool v)
    {
        _voiceWhileMuted = v;
        UpdateBanner();
    }

    void UpdateBanner()
    {
        bool inMuted = _state.MyInputMuted, outMuted = _state.MyOutputMuted;
        bool show = _cfg.MuteWarning && _state.Channel != null && (inMuted || outMuted);
        _banner.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) { _banner.BeginAnimation(OpacityProperty, null); return; }

        bool talkingToMuted = inMuted && _voiceWhileMuted;
        _bannerIcon.Text = inMuted ? "" : "";
        _bannerText.Text = talkingToMuted ? "MÓWISZ — MIKROFON WYCISZONY!"
            : inMuted && outMuted ? "MIKROFON I GŁOŚNIKI WYCISZONE"
            : inMuted ? "MIKROFON WYCISZONY"
            : "GŁOŚNIKI WYCISZONE";
        if (talkingToMuted)
        {
            Touch();
            _banner.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(280))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        }
        else _banner.BeginAnimation(OpacityProperty, null);
    }

    // ---------- autoukrywanie ----------

    /// <summary>Coś się dzieje — pokaż nakładkę w pełni.</summary>
    public void Touch()
    {
        _lastActivity = DateTime.Now;
        if (_idle) SetIdle(false);
    }

    void CheckIdle()
    {
        bool active = !_cfg.AutoHide || _highlight || _voiceWhileMuted
            || _notices.Children.Count > 0 || _state.Members.Any(m => m.Talking);
        if (active)
        {
            _lastActivity = DateTime.Now;
            if (_idle) SetIdle(false);
            return;
        }
        if (!_idle && (DateTime.Now - _lastActivity).TotalSeconds >= Math.Max(1, _cfg.AutoHideSeconds)) SetIdle(true);
    }

    void SetIdle(bool idle)
    {
        _idle = idle;
        if (_cfg.AutoHideMode == "Header")
        {
            _panel.BeginAnimation(OpacityProperty, null);
            _panel.Opacity = 1;
            _members.Visibility = idle ? Visibility.Collapsed : Visibility.Visible;
        }
        else
        {
            _members.Visibility = Visibility.Visible;
            _panel.BeginAnimation(OpacityProperty, new DoubleAnimation(idle ? Math.Clamp(_cfg.AutoHideOpacity, 0, 1) : 1,
                TimeSpan.FromMilliseconds(idle ? 800 : 150)));
        }
    }

    /// <summary>Po zmianie ustawień autoukrywania.</summary>
    public void ResetIdle()
    {
        if (_idle) SetIdle(false);
        _lastActivity = DateTime.Now;
    }

    void UpdatePanelBackground()
    {
        // Tylko mówiący, nikt nie mówi, brak powiadomień — nie zasłaniaj gry pustym panelem.
        bool empty = _members.Children.Count == 0 && _notices.Children.Count == 0;
        bool hideChrome = empty && !_highlight && !_cfg.ShowChannelList && !_preview;
        _panel.Background = hideChrome ? Brushes.Transparent : Theme.B(_t.Panel);
        _channel.Visibility = hideChrome ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---------- dane przykładowe do podglądu ----------

    public static OverlayState DemoState(Config cfg) => new("Pluton 9", new()
    {
        new() { Id = 1, Nickname = "AKI76PL", Talking = true },
        new() { Id = 2, Nickname = "Kamil", Talking = true, Whisper = true },
        new() { Id = 3, Nickname = "Ola", Uid = cfg.Favorites.FirstOrDefault()?.Uid ?? "" },
        new() { Id = 4, Nickname = "Marek", InputMuted = true, OutputMuted = true },
    }, 1, cfg.MuteWarning, false, "serwer");

    public void ShowDemo()
    {
        Render(DemoState(_cfg));
        ClearNotices();
        AddNotice(new("Ola dołączył(a) do kanału", NoticeKind.Join), true);
        AddNotice(new("Piotr rozłączył(a) się", NoticeKind.Leave), true);
        AddNotice(new("Kamil: idziemy na B?", NoticeKind.Message), true);
        AddNotice(new("Marek szturcha Cię!", NoticeKind.Poke), true);
    }
}
