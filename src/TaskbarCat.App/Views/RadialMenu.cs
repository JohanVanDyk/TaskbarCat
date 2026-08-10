using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using TaskbarCat.Models;

using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Image = System.Windows.Controls.Image;
using Point = System.Windows.Point;
using Cursors = System.Windows.Input.Cursors;
// System.Windows.Shapes.Path (the shape) vs System.IO.Path (the file helper) — this file
// wants the file helper, and the Shapes namespace is imported for Ellipse.
using IoPath = System.IO.Path;

namespace TaskbarCat.App.Views;

/// <summary>
/// The click menu: icons arranged on an arc around the cat.
///
/// It is a separate transparent window rather than a Popup inside CatWindow because the
/// cat window is WS_EX_NOACTIVATE and alpha-hit-tested — a menu living inside it would be
/// unclickable everywhere the cat is transparent, and would be clipped to the cat's box.
/// </summary>
internal sealed class RadialMenu : Window
{
    private const double Radius = 86;
    private const double IconSize = 54;

    private readonly Canvas _canvas = new();
    private bool _closing;

    public RadialMenu(string assetsRoot, IReadOnlyList<(string Id, string Label, Stimulus? Stimulus)> items)
    {
        // Titled so tooling can find it; no chrome ever shows it to the user.
        Title = "TaskbarCatMenu";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Manual;
        WindowStartupLocation = WindowStartupLocation.Manual;

        Width = Radius * 2 + IconSize + 24;
        Height = Radius + IconSize + 24;
        Content = _canvas;

        Build(assetsRoot, items);

        // Any click outside, or Escape, dismisses — a pet menu that traps the user is
        // worse than no menu.
        //
        // The _closing guard is not optional: choosing an item calls Close(), which
        // deactivates the window, which re-entered this handler and called Close() again
        // — WPF throws "Cannot set Visibility ... while a Window is closing" and the whole
        // app dies. That fired on every single menu selection.
        Deactivated += (_, _) => { if (!_closing) Close(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && !_closing) Close(); };
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _closing = true;
        base.OnClosing(e);
    }

    /// <summary>Chosen action, or null if dismissed.</summary>
    public string? Result { get; private set; }

    public event Action<string>? Chosen;

    private void Build(string assetsRoot, IReadOnlyList<(string Id, string Label, Stimulus? Stimulus)> items)
    {
        // Fan the icons across a half-circle above the cat, which is where there is
        // always free space — the cat itself sits on the taskbar.
        int n = items.Count;
        double centreX = Width / 2;
        // Pull the arc's centre up by half an icon: the icons at the ends of the sweep
        // sit level with it, and anchoring at the very bottom clipped them in half.
        double centreY = Height - IconSize / 2 - 6;

        for (int i = 0; i < n; i++)
        {
            double t = n == 1 ? 0.5 : i / (double)(n - 1);
            double angle = Math.PI * (1.0 - t);          // pi (left) -> 0 (right)
            double x = centreX + Math.Cos(angle) * Radius - IconSize / 2;
            double y = centreY - Math.Sin(angle) * Radius - IconSize / 2;

            var button = BuildButton(assetsRoot, items[i].Id, items[i].Label);
            Canvas.SetLeft(button, x);
            Canvas.SetTop(button, y);
            _canvas.Children.Add(button);

            // Stagger the entrance so the wheel unfurls instead of popping.
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140))
            {
                BeginTime = TimeSpan.FromMilliseconds(i * 26),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            button.BeginAnimation(OpacityProperty, fade);
        }
    }

    private FrameworkElement BuildButton(string assetsRoot, string id, string label)
    {
        var backing = new Ellipse
        {
            Width = IconSize,
            Height = IconSize,
            Fill = new SolidColorBrush(Color.FromArgb(210, 28, 30, 38)),
            Stroke = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)),
            StrokeThickness = 1.2,
            Effect = new DropShadowEffect
            {
                BlurRadius = 10,
                ShadowDepth = 1,
                Opacity = 0.55,
                Color = Colors.Black,
            },
        };

        var grid = new Grid { Width = IconSize, Height = IconSize, Opacity = 0, Cursor = Cursors.Hand };
        grid.Children.Add(backing);

        var path = IoPath.Combine(assetsRoot, "ui", id + ".png");
        if (File.Exists(path))
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();

            grid.Children.Add(new Image
            {
                Source = bmp,
                Width = IconSize * 0.62,
                Height = IconSize * 0.62,
                Stretch = Stretch.Uniform,
            });
        }
        else
        {
            grid.Children.Add(new TextBlock
            {
                Text = label.Length > 0 ? label[..1] : "?",
                Foreground = Brushes.White,
                FontSize = 20,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
            });
        }

        grid.ToolTip = label;
        grid.MouseEnter += (_, _) => backing.Fill = new SolidColorBrush(Color.FromArgb(235, 58, 62, 78));
        grid.MouseLeave += (_, _) => backing.Fill = new SolidColorBrush(Color.FromArgb(210, 28, 30, 38));
        grid.MouseLeftButtonUp += (_, _) =>
        {
            Result = id;
            Chosen?.Invoke(id);
            if (!_closing) Close();
        };

        return grid;
    }

    /// <summary>Positions the wheel above a point given in DIPs.</summary>
    public void ShowAbove(Point catCentreDip, double catTopDip)
    {
        Left = catCentreDip.X - Width / 2;
        Top = catTopDip - Height + 10;
        Show();
        Activate();
    }
}
