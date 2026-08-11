using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TaskbarCat.App.Interop;
using TaskbarCat.App.Services;

using Image = System.Windows.Controls.Image;
using Brushes = System.Windows.Media.Brushes;

namespace TaskbarCat.App.Views;

/// <summary>
/// The toy itself: a small always-on-top sprite drawn where the pointer is.
///
/// Click-through (WS_EX_TRANSPARENT), because the user must be able to keep working while the
/// cat plays. Without that this window would swallow every click on the desktop.
///
/// It exists at all — rather than just installing the toy as a system cursor — because a yarn
/// ball that does not roll is a worse toy, and re-installing a system cursor every frame is
/// not a real option.
/// </summary>
internal sealed class ToyOverlayWindow : Window
{
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private ToySprite? _toy;
    private int _frame;
    private double _frameClock;
    private IntPtr _hwnd;

    public ToyOverlayWindow()
    {
        Title = "TaskbarCatToy";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        IsHitTestVisible = false;

        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        Content = _image;
        Width = Height = 64;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        int ex = Win32.GetWindowLong(_hwnd, Win32.GWL_EXSTYLE);
        Win32.SetWindowLong(_hwnd, Win32.GWL_EXSTYLE,
            ex | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TRANSPARENT);
    }

    public void SetToy(ToySprite toy)
    {
        _toy = toy;
        _frame = 0;
        _frameClock = 0;
        Width = Height = toy.Size;
        _image.Source = toy.Frames[0];
    }

    /// <summary>Moves to the pointer and advances the toy's own animation.</summary>
    public void Follow(int screenX, int screenY, TimeSpan dt)
    {
        if (_toy is null || _hwnd == IntPtr.Zero) return;

        _frameClock += dt.TotalSeconds;
        double step = 1.0 / Math.Max(1, _toy.Fps);
        while (_frameClock >= step)
        {
            _frameClock -= step;
            _frame = (_frame + 1) % _toy.Frames.Length;
            _image.Source = _toy.Frames[_frame];
        }

        // Physical pixels, centred on the pointer, placed with SetWindowPos for the same reason
        // the cat is: no DIP round-tripping and no drift at 125%.
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int size = (int)Math.Round(_toy.Size * scale);
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST,
            screenX - size / 2, screenY - size / 2, size, size,
            Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
    }
}
