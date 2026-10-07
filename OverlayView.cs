using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using static TS6Overlay.L;

namespace TS6Overlay;

/// <summary>
/// Zawartość nakładki: pasek „mikrofon wyciszony”, serwery z kanałem i osobami, obserwowane kanały,
/// powiadomienia i podgląd pod klawiszem. Używana w oknie nakładki i w podglądzie w ustawieniach.
/// </summary>
public sealed class OverlayView : StackPanel
{
    readonly Config _cfg;
    readonly bool _preview;
    readonly Border _banner = new() { Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 0, 4), Visibility = Visibility.Collapsed };
    readonly TextBlock _bannerIcon = new() { FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 18, Text = "", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Foreground = Brushes.White };
    readonly TextBlock _bannerText = new() { FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
    readonly Border _panel = new() { Padding = new Thickness(10, 8, 10, 8) };
    readonly StackPanel _content = new();
    readonly StackPanel _peek = new() { Visibility = Visibility.Collapsed };
    readonly StackPanel _notices = new();
    readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    Theme _t = Theme.ByName("Terminal");
    OverlayState _state = OverlayState.Disconnected;
    bool _highlight, _voiceWhileMuted, _idle, _peeking;
    string? _hint;
    DateTime _lastActivity = DateTime.Now;
    int _memberRows;

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
        inner.Children.Add(_content);
        inner.Children.Add(_peek);
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

    // ---------- lista: serwery, kanały, osoby ----------

    public void Render(OverlayState s)
    {
        _state = s;
        _content.Children.Clear();
        _memberRows = 0;
        bool anyTalking = false;

        if (_hint != null) _content.Children.Add(Header(_hint));
        if (s.Servers.Count == 0 && _hint == null) _content.Children.Add(Header(T("TeamSpeak: brak połączenia")));
        bool multi = s.Servers.Count > 1;
        foreach (var srv in s.Servers)
        {
            var rows = new List<UIElement>();
            foreach (var m in srv.Members)
            {
                anyTalking |= m.Talking;
                if (!_cfg.ShowChannelList && !m.Talking) continue;
                rows.Add(Row(m, m.Id == srv.MyId, small: false));
            }
            // W trybie „tylko mówiący” serwer bez mówiących nie zajmuje miejsca.
            if (rows.Count == 0 && !_cfg.ShowChannelList && multi) continue;
            if (_content.Children.Count > 0 && multi) _content.Children.Add(new Border { Height = 6 });
            if (multi) _content.Children.Add(Header("🖧 " + (srv.Server != "" ? srv.Server : T("serwer")), small: true, dim: true));
            if (_hint == null || multi) _content.Children.Add(Header(("🔊 " + srv.Channel).TrimEnd()));
            foreach (var r in rows) _content.Children.Add(r);
            _memberRows += rows.Count;

            foreach (var w in srv.Watched)
            {
                var wrows = w.Members.Where(m => _cfg.ShowChannelList || m.Talking).ToList();
                anyTalking |= w.Members.Any(m => m.Talking);
                _content.Children.Add(Header($"👁 {(w.Name == "" ? "…" : w.Name)} ({w.Members.Count})", small: true, top: 6));
                foreach (var m in wrows) _content.Children.Add(Row(m, false, small: true));
                _memberRows += wrows.Count;
            }
        }
        if (anyTalking) Touch();
        if (_idle) SetIdle(true);   // nowe wiersze dostają stan autoukrywania
        UpdateBanner();
        UpdatePanelBackground();
    }

    TextBlock Header(string text, bool small = false, bool dim = false, double top = 0) => new()
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        FontSize = small ? 11 : 12,
        Opacity = dim ? 0.75 : 1,
        Margin = new Thickness(0, top, 0, 4),
        Foreground = Theme.B(_t.Header),
        Effect = TextShadow(),
        TextTrimming = TextTrimming.CharacterEllipsis,
        MaxWidth = 420,
    };

    UIElement Row(ClientInfo m, bool isMe, bool small)
    {
        var fav = _cfg.FavoriteFor(m.Uid);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        string dotColor = m.Talking ? (m.Whisper ? _t.Whisper : _t.Talk) : _t.Idle;
        Shape dot = _t.SquareDots ? new Rectangle() : new Ellipse();
        dot.Width = dot.Height = small ? 8 : 10;
        dot.Margin = new Thickness(small ? 4 : 0, 0, 7, 0);
        dot.VerticalAlignment = VerticalAlignment.Center;
        dot.Fill = Theme.B(dotColor);
        if (m.Talking && _t.Glow) dot.Effect = new DropShadowEffect { Color = Theme.C(dotColor), BlurRadius = 9, ShadowDepth = 0 };
        row.Children.Add(dot);
        row.Children.Add(new TextBlock
        {
            Text = (fav != null ? "★ " : "") + m.Nickname + (isMe ? T(" (ty)") : ""),
            FontSize = small ? 12 : 14,
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

    // ---------- powiadomienia ----------

    (string bg, string prefix) NoticeStyle(NoticeKind k) => k switch
    {
        NoticeKind.Join => (_t.Join, "➜ "),
        NoticeKind.Leave => (_t.Leave, "← "),
        NoticeKind.Message => (_t.Message, "✉ "),
        NoticeKind.Poke => (_t.Poke, "👉 "),
        NoticeKind.Friend => (_t.Join, ""),
        NoticeKind.Watched => (_t.Info, ""),
        _ => (_t.Info, "• "),
    };

    Border NoticeBox(string text, NoticeKind kind)
    {
        var (bg, prefix) = NoticeStyle(kind);
        return new Border
        {
            CornerRadius = new CornerRadius(Math.Min(_t.Radius, 5)),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 4, 0, 0),
            MaxWidth = 420,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Theme.B(bg),
            Child = new TextBlock
            {
                Text = prefix + text,
                Foreground = Theme.B(_t.NoticeText),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                FontWeight = kind == NoticeKind.Poke ? FontWeights.Bold : FontWeights.Normal,
            },
        };
    }

    /// <summary>Dodaje powiadomienie; persistent = nie znika (podgląd w ustawieniach).</summary>
    public void AddNotice(Notice n, bool persistent = false)
    {
        var b = NoticeBox(n.Text, n.Kind);
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

    // ---------- podgląd pod klawiszem ----------

    public bool Peeking => _peeking;

    /// <summary>Pełna lista serwera: niepuste kanały z osobami.</summary>
    public void ShowPeekServer(string server, List<ChannelView> channels)
    {
        _peek.Children.Clear();
        _peek.Children.Add(Header("🖧 " + (server != "" ? server : T("serwer")) + "  " + T("— wszyscy na serwerze")));
        if (channels.Count == 0) _peek.Children.Add(Header(T("Brak osób na serwerze."), small: true, dim: true));
        foreach (var ch in channels)
        {
            _peek.Children.Add(Header((ch.Mine ? "🔊 " : "") + ch.Name + $" ({ch.Members.Count})", small: true, top: 4));
            foreach (var m in ch.Members) _peek.Children.Add(Row(m, false, small: true));
        }
        BeginPeek();
    }

    /// <summary>Ostatnie wiadomości i szturchnięcia.</summary>
    public void ShowPeekMessages(IEnumerable<(DateTime time, string text, NoticeKind kind)> items)
    {
        _peek.Children.Clear();
        _peek.Children.Add(Header("✉ " + T("Ostatnie wiadomości")));
        var list = items.ToList();
        if (list.Count == 0) _peek.Children.Add(Header(T("Brak wiadomości w tej sesji."), small: true, dim: true));
        foreach (var (time, text, kind) in list) _peek.Children.Add(NoticeBox($"{time:HH:mm}  {text}", kind));
        BeginPeek();
    }

    void BeginPeek()
    {
        _peeking = true;
        _notices.Visibility = Visibility.Collapsed;
        _peek.Visibility = Visibility.Visible;
        Touch();
        _panel.BeginAnimation(OpacityProperty, null);
        _panel.Opacity = 1;
        UpdatePanelBackground();
    }

    public void EndPeek()
    {
        if (!_peeking) return;
        _peeking = false;
        _peek.Visibility = Visibility.Collapsed;
        _peek.Children.Clear();
        _notices.Visibility = Visibility.Visible;
        Touch();
        UpdatePanelBackground();
    }

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
        _bannerText.Text = talkingToMuted ? T("MÓWISZ — MIKROFON WYCISZONY!")
            : inMuted && outMuted ? T("MIKROFON I GŁOŚNIKI WYCISZONE")
            : inMuted ? T("MIKROFON WYCISZONY")
            : T("GŁOŚNIKI WYCISZONE");
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
        bool active = !_cfg.AutoHide || _highlight || _voiceWhileMuted || _peeking
            || _notices.Children.Count > 0 || _state.Servers.Any(s => s.Members.Any(m => m.Talking));
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
        bool header = _cfg.AutoHideMode == "Header";
        // „Sam nagłówek”: chowamy wiersze osób (Bordery), zostają nagłówki kanałów.
        foreach (var child in _content.Children.OfType<Border>())
            child.Visibility = idle && header ? Visibility.Collapsed : Visibility.Visible;
        if (header)
        {
            _panel.BeginAnimation(OpacityProperty, null);
            _panel.Opacity = 1;
        }
        else
            _panel.BeginAnimation(OpacityProperty, new DoubleAnimation(idle ? Math.Clamp(_cfg.AutoHideOpacity, 0, 1) : 1,
                TimeSpan.FromMilliseconds(idle ? 800 : 150)));
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
        bool empty = _memberRows == 0 && _notices.Children.Count == 0 && !_peeking;
        bool hideChrome = empty && !_highlight && !_cfg.ShowChannelList && !_preview;
        _panel.Background = hideChrome ? Brushes.Transparent : Theme.B(_t.Panel);
        _content.Visibility = hideChrome || _peeking ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---------- dane przykładowe do podglądu ----------

    public static OverlayState DemoState(Config cfg, bool muted = false) => new(new List<ServerView>
    {
        new(1, "serwer", "Pluton 9", new()
        {
            new() { Id = 1, Nickname = "AKI76PL", Talking = true },
            new() { Id = 2, Nickname = "Kamil", Talking = true, Whisper = true },
            new() { Id = 3, Nickname = "Ola", Uid = cfg.Favorites.FirstOrDefault()?.Uid ?? "" },
            new() { Id = 4, Nickname = "Marek", InputMuted = true, OutputMuted = true },
        }, 1, muted || cfg.MuteWarning, false,
        cfg.WatchedChannels.Count > 0
            ? new List<ChannelView> { new(cfg.WatchedChannels[0], new() { new() { Id = 7, Nickname = "Piotr" }, new() { Id = 8, Nickname = "Ewa" } }) }
            : new List<ChannelView>(),
        true),
    });

    public void ShowDemo()
    {
        Render(DemoState(_cfg));
        ClearNotices();
        AddNotice(new(T("{0} dołączył(a) do kanału", "Ola"), NoticeKind.Join), true);
        AddNotice(new(T("{0} rozłączył(a) się", "Piotr"), NoticeKind.Leave), true);
        AddNotice(new("Kamil: " + T("idziemy na B?"), NoticeKind.Message), true);
        AddNotice(new(T("{0} szturcha Cię!", "Marek"), NoticeKind.Poke), true);
    }
}
