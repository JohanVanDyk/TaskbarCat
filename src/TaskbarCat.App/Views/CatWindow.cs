using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TaskbarCat.App.Interop;
using TaskbarCat.App.Services;
using TaskbarCat.Models;
using TaskbarCat.Services;

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
    private int _chonk;
    private double _perch;      // 0 = screen edge, 1 = on top of the bar
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

        MouseLeftButtonUp += (_, _) => { ClickCount++; CatClicked?.Invoke(); };
        MouseRightButtonUp += (_, _) => { ClickCount++; CatClicked?.Invoke(); };
    }

    /// <summary>Raised when the travelable rail length changes (taskbar or DPI change).</summary>
    public event Action<double>? SpanChanged;

    /// <summary>Raised when the user clicks the opaque part of the cat.</summary>
    public event Action? CatClicked;

    /// <summary>Raised when the user picks an item from the radial menu.</summary>
    public event Action<string>? MenuChosen;

    public TaskbarRail? Rail => _taskbar?.Rail;

    /// <summary>True while the taskbar is on screen. Always true for a bar that does not hide.</summary>
    public bool BarRevealed => _taskbar?.BarRevealed ?? true;

    /// <summary>Raised when an auto-hide bar slides in or out.</summary>
    public event Action<bool>? BarRevealedChanged;

    /// <summary>The cat's top edge in physical screen pixels. Toy mode compares the pointer to it.</summary>
    public double CatTopPhysical => Top * Scale;

    /// <summary>Checks whether the bar has slid. Driven from the controller's tick.</summary>
    public void PollTaskbarReveal() => _taskbar?.PollReveal();

    /// <summary>Travelable pixels along the rail, i.e. rail length minus the cat's width.</summary>
    public double TravelSpan
    {
        get
        {
            if (_taskbar is null) return 0;
            var rail = _taskbar.Rail;
            var (fatW, fatH) = CurrentStretch();
            int catW = (int)Math.Round(_clip.FrameWidth * Scale * fatW);
            int catH = (int)Math.Round(_clip.FrameHeight * Scale * fatH);
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

    /// <summary>
    /// Clicks that reached WPF, menus opened, and raw button messages the window was handed.
    /// Kept past the bug they were added for: when the hit test is wrong the window receives
    /// WM_NCHITTEST and NOTHING else, so raw.buttonmsgs=0 in a self-test report is the
    /// signature of the cat going click-through again.
    /// </summary>
    internal int ClickCount { get; private set; }
    internal int MenuOpenCount { get; private set; }

    /// <summary>
    /// Supplies the cat's needs when the wheel opens. A pull rather than a pushed snapshot:
    /// the meters move every tick, and the menu must show them as they are at the click, not
    /// as they were when someone last remembered to update this window.
    /// </summary>
    public Func<MoodSnapshot>? MoodProvider { get; set; }

    /// <summary>
    /// Fires a menu choice as if it had been clicked. Self-test only: the wheel's buttons cannot
    /// be clicked headlessly — the taskbar is auto-hide, so moving the pointer to the cat's row
    /// pops the bar up over it and eats the click — and this is the only path that exercises
    /// what a choice does to the rest of the app rather than just what the wheel draws.
    /// </summary>
    internal void ChooseMenuItem(string id) => MenuChosen?.Invoke(id);
    internal int RawButtonMessages { get; private set; }

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
        _taskbar.BarRevealedChanged += revealed =>
        {
            BarRevealedChanged?.Invoke(revealed);
            RaiseSpanIfChanged();
        };

        Reposition();
        RaiseSpanIfChanged();
    }

    /// <summary>
    /// How much wider and taller the sprite is drawn, by chonk level, for clips with no drawn
    /// chonk art. The table lives in Core (<see cref="ChonkVisuals"/>) because it is measured
    /// off the art rather than chosen, and because it has to be checkable without a window.
    ///
    /// Stretching rather than drawing three more sets of sheets is a deliberate trade: it is
    /// 45 sheets per chonk level otherwise, across every clip and every coat, and every one of
    /// them would have to stay in sync with the others.
    /// </summary>
    private (double W, double H) ChonkStretch(int level) => ChonkVisuals.Stretch(level, _clip.Sprawl);

    /// <summary>
    /// The stretch actually applied to the frame on screen. Clips with drawn chonk art are
    /// already fat and render 1:1; stretching those too would fatten them twice. A level is a
    /// mix — only the clips worth drawing were drawn — so this is per clip, not per level.
    /// </summary>
    private (double W, double H) CurrentStretch() =>
        _clip.ChonkArt ? (1.0, 1.0) : ChonkStretch(_chonk);

    /// <summary>
    /// How far the cat has climbed onto a revealed auto-hide taskbar: 0 on the screen edge,
    /// 1 standing on the bar. Driven by PerchAnimator, so it passes 1 briefly at the top of
    /// the jump and the cat rises above the bar before settling onto it.
    /// </summary>
    public double Perch
    {
        get => _perch;
        set
        {
            if (Math.Abs(value - _perch) < 0.0005) return;
            _perch = value;
            Reposition();
        }
    }

    /// <summary>Chonk level, 0-3. Set by the controller; changes re-place the window.</summary>
    public int ChonkLevel
    {
        get => _chonk;
        set
        {
            int lvl = Math.Clamp(value, 0, 3);
            if (lvl == _chonk) return;
            _chonk = lvl;
            Reposition();
            RaiseSpanIfChanged();
        }
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
        // A drawn-chonk clip is rendered unstretched while its neighbours are stretched, and an
        // upright pose is stretched across where a sprawled one is stretched down, so the window
        // footprint changes on either swap even though the frame size has not.
        bool stretchChanged = clip.ChonkArt != _clip.ChonkArt || clip.Sprawl != _clip.Sprawl;
        bool sizeChanged = clip.FrameWidth != _clip.FrameWidth || clip.FrameHeight != _clip.FrameHeight;
        _clip = clip;
        _frame = Math.Clamp(frameIndex, 0, clip.Frames.Length - 1);
        _image.Source = clip.Frames[_frame];

        // DPI resolves late and unpredictably: the same WPF device transform reads 1.0
        // during OnSourceInitialized *and* OnContentRendered, then 1.25 seconds later.
        // Rather than guess the right lifecycle event, notice the change and re-place.
        // This also covers the cat being dragged to a monitor with a different scale.
        if (sizeChanged || stretchChanged || Math.Abs(Scale - _lastScale) > 0.001)
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

        // Order is the arc, left to right. The two that are not interactions go on the far
        // right, away from the four the user reaches for constantly — Close especially, since
        // a mis-click there costs them the cat.
        var items = new List<(string, string, Stimulus?)>
        {
            ("feed", "Feed", Stimulus.Fed),
            ("brush", "Brush", Stimulus.Brushed),
            ("pet", "Pet", Stimulus.Petted),
            ("play", "Yarn (chase the pointer)", Stimulus.PlayToyOffered),
            ("laser", "Laser pointer", null),
            ("customize", "Customize", null),
            ("close", "Close (quit)", null),
        };

        MenuOpenCount++;
        _menu = new RadialMenu(_assetsRoot, items, MoodProvider?.Invoke());
        _menu.Chosen += id => MenuChosen?.Invoke(id);
        _menu.Closed += (_, _) => _menu = null;

        _menu.ShowAbove(new Point(Left + Width / 2, 0), Top);
    }

    public void Reposition()
    {
        if (_taskbar is null || _hwnd == IntPtr.Zero) return;

        double scale = Scale;
        _lastScale = scale;
        var (fatW, fatH) = CurrentStretch();
        int catW = (int)Math.Round(_clip.FrameWidth * scale * fatW);
        int catH = (int)Math.Round(_clip.FrameHeight * scale * fatH);

        int screenW = (int)Math.Round(SystemParameters.PrimaryScreenWidth * scale);
        int screenH = (int)Math.Round(SystemParameters.PrimaryScreenHeight * scale);

        // Lerp between standing on the screen edge and standing on the bar. Doing it here, on
        // two full placements, keeps the maths edge-agnostic: a bottom bar lifts the cat, a top
        // bar pushes it down, and neither this nor the animator has to know which.
        var down = RailPlacement.Resting(_taskbar.Rail, catW, catH, _alongRail, screenW, screenH, false);
        var up = RailPlacement.Resting(_taskbar.Rail, catW, catH, _alongRail, screenW, screenH, true);

        int x = down.X + (int)Math.Round((up.X - down.X) * _perch);
        int y = down.Y + (int)Math.Round((up.Y - down.Y) * _perch);

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
        // Raw button messages, counted before anything else can swallow them. Compared with
        // ClickCount this says whether a lost click died in Win32 or inside WPF's input stack.
        if (msg is 0x0201 or 0x0202 or 0x00A1 or 0x00A2) RawButtonMessages++;

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

        // PointFromScreen already takes DEVICE pixels and returns local DIPs. Converting with
        // TransformFromDevice first and then calling it applied the DPI divide twice, so at
        // 125% every hit test landed at 0.8x of the real point — up and left, in the sprite's
        // transparent corner. The window then answered HTTRANSPARENT for its own body and the
        // system routed every click to whatever was behind it: clicking the cat did nothing.
        var local = PointFromScreen(new Point(px, py));

        // Window DIP size equals the frame's pixel size by construction (we size the window to
        // frame*scale physical) — EXCEPT when the cat is chonky, where the window is stretched
        // around the same sheet. Undo the stretch before indexing, or the mask is sampled at
        // the wrong pixel and the fat cat is unclickable down its sides.
        var (fatW, fatH) = CurrentStretch();
        int x = (int)(local.X / fatW), y = (int)(local.Y / fatH);
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
