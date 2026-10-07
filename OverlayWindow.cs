using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace TS6Overlay;

/// <summary>Przezroczyste okno na wierzchu, które przepuszcza kliknięcia do gry.</summary>
public sealed class OverlayWindow : Window
{
    readonly Config _cfg;
    readonly DispatcherTimer _ctrlTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    bool _editMode, _ctrlDrag;

    public OverlayView View { get; }
    /// <summary>Użytkownik przesunął nakładkę — App zapisuje pozycję w ustawieniach albo w profilu gry.</summary>
    public event Action<double, double>? Moved;

    public OverlayWindow(Config cfg)
    {
        _cfg = cfg;
        Title = "TS6 Overlay";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Left = cfg.Left;
        Top = cfg.Top;

        View = new OverlayView(cfg);
        Content = View;
        ApplyScale(cfg.Scale, cfg.Opacity);
        View.ApplyTheme(Theme.ByName(cfg.Theme));

        MouseLeftButtonDown += (_, e) =>
        {
            if (!(_editMode || _ctrlDrag) || e.ButtonState != MouseButtonState.Pressed) return;
            DragMove();
            Moved?.Invoke(Left, Top);
        };
        SourceInitialized += (_, _) => ApplyClickThrough();

        // Ctrl + mysz nad nakładką = można ją chwycić i przesunąć.
        _ctrlTimer.Tick += (_, _) => CheckCtrlDrag();
        _ctrlTimer.Start();
    }

    void CheckCtrlDrag()
    {
        if (_editMode || !IsVisible) return;
        bool ctrl = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
        bool dragging = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0 && _ctrlDrag;
        bool over = false;
        if (ctrl && GetCursorPos(out var pt))
        {
            var h = new WindowInteropHelper(this).Handle;
            over = GetWindowRect(h, out var r) && pt.X >= r.Left && pt.X < r.Right && pt.Y >= r.Top && pt.Y < r.Bottom;
        }
        bool want = (ctrl && over) || dragging;
        if (want == _ctrlDrag) return;
        _ctrlDrag = want;
        View.SetHighlight(want);
        ApplyClickThrough();
    }

    public bool EditMode
    {
        get => _editMode;
        set
        {
            _editMode = value;
            View.SetHighlight(value, value ? L.T("Przeciągnij mnie myszką, potem „Zablokuj pozycję” w zasobniku") : null);
            ApplyClickThrough();
            if (!value) Moved?.Invoke(Left, Top);
        }
    }

    public void ApplyScale(double scale, double opacity)
    {
        View.LayoutTransform = new ScaleTransform(scale, scale);
        Opacity = opacity;
    }

    // ---------- click-through ----------

    const int GWL_EXSTYLE = -20;
    const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    const int VK_CONTROL = 0x11, VK_LBUTTON = 0x01;

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

    void ApplyClickThrough()
    {
        var h = new WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero) return;
        int ex = GetWindowLong(h, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        ex = _editMode || _ctrlDrag ? ex & ~WS_EX_TRANSPARENT : ex | WS_EX_TRANSPARENT;
        SetWindowLong(h, GWL_EXSTYLE, ex);
    }
}
