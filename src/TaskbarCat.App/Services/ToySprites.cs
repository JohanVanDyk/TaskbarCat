using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace TaskbarCat.App.Services;

/// <summary>One toy's frames, sliced. Toys are 64x64, not the cat's 160x128.</summary>
internal sealed class ToySprite
{
    public required string Id { get; init; }
    public required BitmapSource[] Frames { get; init; }
    public required int Size { get; init; }
    public required int Fps { get; init; }
}

/// <summary>
/// Loads assets/toys. Separate from SpriteLibrary on purpose: toys are not the cat, they are
/// not coat-dependent, and they use their own frame size — folding them into the cat's clip
/// dictionary would mean every consumer of that dictionary had to know which entries were not
/// cats.
/// </summary>
internal static class ToySprites
{
    private sealed record ToyDef(string Id, string Sheet, int Frames, int Fps, int FrameWidth, int FrameHeight);

    public static IReadOnlyDictionary<string, ToySprite> Load(string assetsRoot)
    {
        var result = new Dictionary<string, ToySprite>(StringComparer.OrdinalIgnoreCase);

        var manifestPath = Path.Combine(assetsRoot, "sprites.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        if (!doc.RootElement.TryGetProperty("toys", out var toys)) return result;

        foreach (var el in toys.EnumerateArray())
        {
            var def = el.Deserialize<ToyDef>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (def is null) continue;

            var path = Path.Combine(assetsRoot, "toys", def.Sheet);
            if (!File.Exists(path)) continue;

            var sheet = new BitmapImage();
            sheet.BeginInit();
            sheet.UriSource = new Uri(path, UriKind.Absolute);
            sheet.CacheOption = BitmapCacheOption.OnLoad;
            sheet.EndInit();
            sheet.Freeze();

            var frames = new BitmapSource[def.Frames];
            for (int i = 0; i < def.Frames; i++)
            {
                var crop = new CroppedBitmap(sheet,
                    new System.Windows.Int32Rect(i * def.FrameWidth, 0, def.FrameWidth, def.FrameHeight));
                crop.Freeze();
                frames[i] = crop;
            }

            result[def.Id] = new ToySprite
            {
                Id = def.Id,
                Frames = frames,
                Size = def.FrameWidth,
                Fps = def.Fps,
            };
        }

        return result;
    }
}
