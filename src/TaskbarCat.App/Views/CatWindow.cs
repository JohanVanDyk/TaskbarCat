using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TaskbarCat.App.Interop;
using TaskbarCat.App.Services;
using TaskbarCat.Models;

// UseWindowsForms (for the tray icon) drags System.Drawing/System.Windows.Forms into
// scope, which collides with WPF on Image/Application/MessageBox. Alias, don't guess.
using Image = System.Windows.Controls.Image;
using Point = System.Windows.Point;
using Brushes = System.Windows.Media.Brushes;

namespace TaskbarCat.App.Views;

/// <summary>
/// The cat's window. Deliberately passive: it renders whatever frame it is handed and
/// reports user input upward. All decisions live in CatController and the Core services.
///
/// Built in code rather than XAML because every property here is load-bearing for
/// transparency or hit-testing, and markup would bury that in attribute noise.
/// </summary>
internal sealed class CatWindow : Window
{
    // Stretch.Fill + Stretch alignment, NOT Stretch.None. The window is sized in physical
    // pixels (SetWindowPos) while WPF lays out in DIPs; at 125% those disagree and a
    // natural-size image overflows its window, showing a zoomed crop of the sprite.
    // Filling the window makes the bitmap track whatever physical size we asked for.
    private readonly Image _image = new()
    {
        Stretch = Stretch.Fill,
        HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
        VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
    };

    private readonly string _assetsRoot;

    private HwndSource? _source;
    private TaskbarWatcher? _taskbar;
    private IntPtr _hwnd;

    private Clip _clip;
    private int _frame;
    private double _alongRail = 0.5;
    private double _lastScale;
    private double _lastSpan = -1;
    private RadialMenu? _menu;

    public CatWindow(SpriteLibrary sprites, string assetsRoot)
    {
        _assetsRoot = assetsRoot;
        _clip = sprites.Clips.TryGetValue("sleep", out var sleep) ? sleep : sprites.Clips.Values.First();

        // No chrome shows it, but the title is how tooling (and the capture script)
        // finds the window — FindWindow needs something to match on.
        Title = "TaskbarCat";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SnapsToDevicePixels = true;

        Width = _clip.FrameWidth;
        Height = _clip.FrameHeight;

        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        _image.Source = _clip.Frames[0];
        Content = _image;

        MouseLeftButtonUp += (_, _) => CatClicked?.Invoke();
        MouseRightButtonUp += (_, _) => CatClicked?.Invoke();
    }

    /// <summary>Raised when the travelable rail length changes (taskbar or DPI change).</summary>
    public event Action<double>? SpanChanged;

    /// <summary>Raised when the user clicks the opaque part of the cat.</summary>
    public event Action? CatClicked;

    /// <summary>Raised when the user picks an item from the radial menu.</summary>
    public event Action<string>? MenuChosen;

    public TaskbarRail? Rail => _taskbar?.Rail;

    /// <summary>Travelable pixels along the rail, i.e. rail length minus the cat's width.</summary>
    public double TravelSpan
    {
        get
        {
            if (_taskbar is null) return 0;
            var rail = _taskbar.Rail;
            int catW = (int)Math.Round(_clip.FrameWidth * Scale);
            int catH = (int)Math.Round(_clip.FrameHeight * Scale);
            return Math.Max(0, rail.IsHorizontal ? rail.Width - catW : rail.Height - catH);
        }
    }

    /// <summary>
    /// Physical pixels per DIP for the monitor the cat is on. Taken from WPF's own device
    /// transform rather than GetDpiForWindow: WPF maintains it correctly across
    /// per-monitor DPI changes, and it is the same number WPF uses for layout — so the
    /// window size and the sprite size cannot disagree.
    /// </summary>
    internal double Scale
    {
        get
        {
            var m = _source?.CompositionTarget?.TransformToDevice.M11 ?? 0;
            if (m > 0) return m;
            return _hwnd == IntPtr.Zero ? 1.0 : Math.Max(1, Win32.GetDpiForWindow(_hwnd)) / 96.0;
        }
    }

    internal uint RawDpi => _hwnd == IntPtr.Zero ? 0 : Win32.GetDpiForWindow(_hwnd);
    internal int ScreenPhysicalWidth => (int)Math.Round(SystemParameters.PrimaryScreenWidth * Scale);
    internal int ScreenPhysicalHeight => (int)Math.Round(SystemParameters.PrimaryScreenHeight * Scale);
    internal string LastPlacement { get; private set; } = "(never)";
    internal int RepositionCount { get; private set; }
    internal string CurrentClipId => _clip.Id;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _hwnd = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);

        // Keep the cat out of Alt-Tab and stop it stealing focus when clicked.
        int ex = Win32.GetWindowLong(_hwnd, Win32.GWL_EXSTYLE);
        Win32.SetWindowLong(_hwnd, Win32.GWL_EXSTYLE, ex | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE);

        _taskbar = new TaskbarWatcher(_hwnd);
        _taskbar.RailChanged += _ => { Reposition(); RaiseSpanIfChanged(); };
        _taskbar.FullScreenChanged += full => Visibility = full ? Visibility.Hidden : Visibility.Visible;

        Reposition();
        RaiseSpanIfChanged();
    }

    /// <summary>Sets the cat's normalised position along the rail and re-places it.</summary>
    public void SetAlong(double along)
    {
        _alongRail = Math.Clamp(along, 0, 1);
        Reposition();
    }

    /// <summary>Shows a specific frame of a clip. Called by the animation player.</summary>
    public void ShowClip(Clip clip, int frameIndex)
    {
        bool sizeChanged = clip.FrameWidth != _clip.FrameWidth || clip.FrameHeight != _clip.FrameHeight;
        _clip = clip;
        _frame = Math.Clamp(frameIndex, 0, clip.Frames.Length - 1);
        _image.Source = clip.Frames[_frame];

        // DPI resolves late and unpredictably: the same WPF device transform reads 1.0
        // during OnSourceInitialized *and* OnContentRendered, then 1.25 seconds later.
        // Rather than guess the right lifecycle event, notice the change and re-place.
        // This also covers the cat being dragged to a monitor with a different scale.
        if (sizeChanged || Math.Abs(Scale - _lastScale) > 0.001)
        {
            Reposition();
            RaiseSpanIfChanged();
        }
    }

    public void ShowRadialMenu()
    {
        if (_menu is { IsVisible: true })
        {
            _menu.Close();
            _menu = null;
            return;
        }

        var items = new List<(string, string, Stimulus?)>
        {
            ("feed", "Feed", Stimulus.Fed),
            ("brush", "Brush", Stimulus.Brushed),
            ("pet", "Pet", Stimulus.Petted),
            ("play", "Play", Stimulus.PlayToyOffered),
        };

        _menu = new RadialMenu(_assetsRoot, items);
        _menu.Chosen += id => MenuChosen?.Invoke(id);
        _menu.Closed += (_, _) => _menu = null;

        _menu.ShowAbove(new Point(Left + Width / 2, 0), Top);
    }

    public void Reposition()
    {
        if (_taskbar is null || _hwnd == IntPtr.Zero) return;

        double scale = Scale;
        _lastScale = scale;
        int catW = (int)Math.Round(_clip.FrameWidth * scale);
        int catH = (int)Math.Round(_clip.FrameHeight * scale);

        int screenW = (int)Math.Round(SystemParameters.PrimaryScreenWidth * scale);
        int screenH = (int)Math.Round(SystemParameters.PrimaryScreenHeight * scale);

        var (x, y) = RailPlacement.Resting(_taskbar.Rail, catW, catH, _alongRail, screenW, screenH);

        // Size AND position in physical pixels: no DIP round-tripping, no drift at
        // 125%/150%, and the sprite lands on its pixel grid unscaled.
        bool ok = Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, x, y, catW, catH,
            Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);

        RepositionCount++;
        LastPlacement = $"scale={scale} cat={catW}x{catH} xy={x},{y} " +
                        $"screen={screenW}x{screenH} swp={ok}";
    }

    private void RaiseSpanIfChanged()
    {
        double span = TravelSpan;
        if (Math.Abs(span - _lastSpan) < 0.5) return;
        _lastSpan = span;
        SpanChanged?.Invoke(span);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        _taskbar?.WndProc(hwnd, msg, wParam, lParam, ref handled);

        if (msg == Win32.WM_DPICHANGED)
        {
            Reposition();
            RaiseSpanIfChanged();
        }
        else if (msg == Win32.WM_NCHITTEST)
        {
            // Clicks on transparent pixels must reach whatever is behind the cat, or a
            // 160x128 invisible box eats taskbar clicks. Alpha decides, not bounds.
            handled = true;
            return IsOnCat(lParam) ? Win32.HTCLIENT : Win32.HTTRANSPARENT;
        }

        return IntPtr.Zero;
    }

    private bool IsOnCat(IntPtr lParam)
    {
        if (_source?.CompositionTarget is null) return true;

        // lParam packs screen coords in physical pixels (signed, for multi-monitor).
        int px = unchecked((short)(lParam.ToInt64() & 0xFFFF));
        int py = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));

        var dipScreen = _source.CompositionTarget.TransformFromDevice.Transform(new Point(px, py));
        var local = PointFromScreen(dipScreen);

        // Window DIP size equals the frame's pixel size by construction (we size the
        // window to frame*scale physical), so local DIPs index the alpha mask directly.
        int x = (int)local.X, y = (int)local.Y;
        if (x < 0 || y < 0 || x >= _clip.FrameWidth || y >= _clip.FrameHeight) return false;

        byte alpha = _clip.AlphaMasks[_frame][y * _clip.FrameWidth + x];
        return alpha > 8;
    }

    protected override void OnClosed(EventArgs e)
    {
        _menu?.Close();
        _taskbar?.Dispose();
        _source?.RemoveHook(WndProc);
        base.OnClosed(e);
    }
}
