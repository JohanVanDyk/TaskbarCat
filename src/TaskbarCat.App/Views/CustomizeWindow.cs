using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using TaskbarCat.Models;

// UseWPF and UseWindowsForms are both on (WinForms only for the tray's NotifyIcon), so every
// name the two frameworks share has to be pinned per file.
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Image = System.Windows.Controls.Image;
using TextBox = System.Windows.Controls.TextBox;
using Orientation = System.Windows.Controls.Orientation;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace TaskbarCat.App.Views;

/// <summary>
/// Name the cat and choose its coat.
///
/// A normal activatable window, unlike everything else in this app: it has text entry, so it
/// needs focus, which is exactly what the cat window's WS_EX_NOACTIVATE forbids. Built in code
/// like the rest — the project has no XAML, and one markup file for one dialog would mean
/// carrying an App.xaml build path for it.
///
/// Applies live rather than on OK. Picking a coat you cannot see until you close the dialog is
/// a guessing game, and the swap is cheap.
/// </summary>
internal sealed class CustomizeWindow : Window
{
    private const int PreviewSize = 96;

    private readonly TextBox _name;
    private readonly IReadOnlyList<ColorPreset> _presets;
    private readonly Dictionary<string, Border> _tiles = new(StringComparer.OrdinalIgnoreCase);

    private readonly string _originalName;
    private readonly string _originalPreset;

    public CustomizeWindow(string assetsRoot, IReadOnlyList<ColorPreset> presets, string name, string presetId)
    {
        _presets = presets;
        _originalName = name;
        _originalPreset = presetId;

        Title = "Customize your cat";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        Topmost = true;
        Background = new SolidColorBrush(Color.FromRgb(28, 30, 38));
        Foreground = Brushes.White;

        var root = new StackPanel { Margin = new Thickness(22) };

        root.Children.Add(new TextBlock
        {
            Text = "Name",
            Foreground = new SolidColorBrush(Color.FromRgb(170, 176, 190)),
            Margin = new Thickness(2, 0, 0, 6),
        });

        _name = new TextBox
        {
            Text = name,
            MaxLength = 24,
            FontSize = 15,
            Padding = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Color.FromRgb(44, 47, 58)),
            Foreground = Brushes.White,
            CaretBrush = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(80, 86, 102)),
            BorderThickness = new Thickness(1),
        };
        _name.TextChanged += (_, _) => NameChanged?.Invoke(EffectiveName);
        root.Children.Add(_name);

        root.Children.Add(new TextBlock
        {
            Text = "Coat",
            Foreground = new SolidColorBrush(Color.FromRgb(170, 176, 190)),
            Margin = new Thickness(2, 18, 0, 8),
        });

        var strip = new WrapPanel { MaxWidth = (PreviewSize + 22) * 3 };
        foreach (var preset in presets)
        {
            var tile = BuildTile(assetsRoot, preset);
            _tiles[preset.Id] = tile;
            strip.Children.Add(tile);
        }
        root.Children.Add(strip);
        Select(presetId, notify: false);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0),
        };
        buttons.Children.Add(BuildButton("Cancel", isPrimary: false, onClick: () => { Revert(); Close(); }));
        buttons.Children.Add(BuildButton("Done", isPrimary: true, onClick: Close));
        root.Children.Add(buttons);

        Content = root;

        _name.Loaded += (_, _) => { _name.Focus(); _name.SelectAll(); };

        // Escape reverts, matching Cancel: the changes are already live, so dismissing the
        // dialog without undoing them would leave the user with a cat they did not choose.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Revert(); Close(); }
            else if (e.Key == Key.Enter) Close();
        };
    }

    /// <summary>Live as the user types. Blank falls back rather than leaving a nameless cat.</summary>
    public event Action<string>? NameChanged;

    /// <summary>Live as the user picks; the caller reloads the sprite library.</summary>
    public event Action<string>? PresetChanged;

    public string EffectiveName =>
        string.IsNullOrWhiteSpace(_name.Text) ? "Cat" : _name.Text.Trim();

    private void Revert()
    {
        _name.Text = _originalName;
        NameChanged?.Invoke(_originalName);
        Select(_originalPreset, notify: true);
    }

    private void Select(string presetId, bool notify)
    {
        foreach (var (id, tile) in _tiles)
        {
            bool on = string.Equals(id, presetId, StringComparison.OrdinalIgnoreCase);
            tile.BorderBrush = on
                ? new SolidColorBrush(Color.FromRgb(120, 190, 255))
                : new SolidColorBrush(Color.FromRgb(70, 75, 90));
            tile.BorderThickness = new Thickness(on ? 2.5 : 1);
        }

        if (notify && _tiles.ContainsKey(presetId)) PresetChanged?.Invoke(presetId);
    }

    private Border BuildTile(string assetsRoot, ColorPreset preset)
    {
        var stack = new StackPanel { Width = PreviewSize };

        var thumb = LoadPreview(assetsRoot, preset);
        if (thumb is not null)
        {
            stack.Children.Add(new Image
            {
                Source = thumb,
                Width = PreviewSize - 16,
                Height = (PreviewSize - 16) * 0.8,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 4, 0, 2),
            });
        }

        stack.Children.Add(new TextBlock
        {
            Text = preset.Name,
            Foreground = Brushes.White,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(2, 0, 2, 6),
        });

        var tile = new Border
        {
            Child = stack,
            Margin = new Thickness(0, 0, 10, 10),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(38, 41, 51)),
            Cursor = Cursors.Hand,
            BorderBrush = new SolidColorBrush(Color.FromRgb(70, 75, 90)),
            BorderThickness = new Thickness(1),
        };

        if (preset.Notes is { Length: > 0 }) tile.ToolTip = preset.Notes;

        tile.MouseLeftButtonUp += (_, _) => Select(preset.Id, notify: true);
        return tile;
    }

    /// <summary>
    /// First frame of the preset's sit_look sheet, or whatever sheet is there. Read straight
    /// off disk rather than through SpriteLibrary: slicing every clip of every coat just to
    /// draw three thumbnails would stall the UI thread on open.
    /// </summary>
    private static BitmapSource? LoadPreview(string assetsRoot, ColorPreset preset)
    {
        var dir = Path.Combine(assetsRoot, preset.SheetDir.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            var sheet = Path.Combine(dir, "sit_look.png");
            if (!File.Exists(sheet))
            {
                sheet = Directory.EnumerateFiles(dir, "*.png").FirstOrDefault() ?? string.Empty;
                if (sheet.Length == 0) return null;
            }

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(sheet, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();

            // Sheets are horizontal strips; a whole strip as a thumbnail is a row of tiny cats.
            int fw = Math.Min(160, bmp.PixelWidth);
            var frame = new CroppedBitmap(bmp, new Int32Rect(0, 0, fw, bmp.PixelHeight));
            frame.Freeze();
            return frame;
        }
        catch (Exception ex) when (ex is IOException or DirectoryNotFoundException or NotSupportedException or UriFormatException)
        {
            return null;   // a coat with unreadable art still gets a named, pickable tile
        }
    }

    private static FrameworkElement BuildButton(string text, bool isPrimary, Action onClick)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(18, 7, 18, 7),
            Margin = new Thickness(8, 0, 0, 0),
            Cursor = Cursors.Hand,
            Background = new SolidColorBrush(isPrimary
                ? Color.FromRgb(64, 122, 190)
                : Color.FromRgb(52, 56, 68)),
            Child = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
            Effect = isPrimary ? new DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Opacity = 0.4 } : null,
        };

        border.MouseLeftButtonUp += (_, _) => onClick();
        return border;
    }
}
