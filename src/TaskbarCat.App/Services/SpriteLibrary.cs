using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TaskbarCat.Models;

namespace TaskbarCat.App.Services;

/// <summary>One animation clip, sliced and ready to render.</summary>
internal sealed class Clip
{
    public required string Id { get; init; }
    public required BitmapSource[] Frames { get; init; }
    /// <summary>Per-frame alpha, one byte per pixel, row-major. Drives the click hit-test.</summary>
    public required byte[][] AlphaMasks { get; init; }
    public required int FrameWidth { get; init; }
    public required int FrameHeight { get; init; }
    public required int Fps { get; init; }
    public required bool Loop { get; init; }

    /// <summary>
    /// True when this clip was loaded from a drawn chonk sheet rather than the normal one.
    /// The renderer stretches the normal sheets to fake an overfed cat; a clip that has real
    /// chonk art must NOT be stretched on top of that, or it is fattened twice.
    /// </summary>
    public bool ChonkArt { get; init; }

    /// <summary>
    /// True for poses drawn stretched out along the ground. Decides which way an overfed cat is
    /// stretched when this clip has no drawn chonk art: across for an upright pose, downward for
    /// a sprawled one, because widening a sprawl makes the cat longer rather than fatter.
    /// </summary>
    public bool Sprawl { get; init; }
}

/// <summary>
/// Loads sprites.json and slices each strip into frozen frames. Everything is decoded
/// once at startup and Freeze()d: no per-frame decoding, no cross-thread affinity, and
/// the render loop allocates nothing.
/// </summary>
internal sealed class SpriteLibrary
{
    private readonly Dictionary<string, Clip> _clips = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, Clip> Clips => _clips;

    /// <summary>Id of the preset these clips were sliced from. Survives a swap so the UI can
    /// show which coat is actually on screen, not which one was last requested.</summary>
    public string PresetId { get; private set; } = string.Empty;

    private static SpriteManifest ReadManifest(string assetsRoot)
    {
        var manifestPath = Path.Combine(assetsRoot, "sprites.json");
        var json = File.ReadAllText(manifestPath);
        return JsonSerializer.Deserialize<SpriteManifest>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        }) ?? throw new InvalidDataException($"Could not parse {manifestPath}");
    }

    /// <summary>
    /// The coats on offer, in manifest order. Only those whose sheet directory actually
    /// exists: the manifest is the design intent, the disk is the truth, and a picker that
    /// offers a coat with no art produces an invisible cat.
    /// </summary>
    public static IReadOnlyList<ColorPreset> LoadPresets(string assetsRoot)
    {
        return ReadManifest(assetsRoot).ColorPresets
            .Where(p => Directory.Exists(Path.Combine(assetsRoot, p.SheetDir.Replace('/', Path.DirectorySeparatorChar))))
            .ToList();
    }

    /// <summary>Chonk level these clips were loaded for. 0 is the normal-weight cat.</summary>
    public int ChonkLevel { get; private set; }

    /// <summary>How many clips came from drawn chonk art. 0 means everything is being stretched.</summary>
    public int ChonkArtClips { get; private set; }

    public static SpriteLibrary Load(string assetsRoot, string? presetId = null, int chonkLevel = 0)
    {
        var manifest = ReadManifest(assetsRoot);

        var preset = manifest.ColorPresets.FirstOrDefault(p => p.Id == presetId)
                     ?? manifest.ColorPresets.FirstOrDefault(p => p.IsDefault)
                     ?? manifest.ColorPresets.FirstOrDefault()
                     ?? throw new InvalidDataException("sprites.json declares no colour presets.");

        var lib = new SpriteLibrary { PresetId = preset.Id, ChonkLevel = chonkLevel };
        var coatDir = Path.Combine(assetsRoot, preset.SheetDir.Replace('/', Path.DirectorySeparatorChar));

        foreach (var clip in manifest.Clips)
        {
            // Drawn chonk art wins where it exists; everything else falls back to the normal
            // sheet and gets stretched. Only the clips worth drawing were drawn, so a level is
            // always a mix of the two.
            var path = Path.Combine(coatDir, clip.Sheet);
            bool chonkArt = false;

            if (chonkLevel > 0)
            {
                var fat = Path.Combine(coatDir, $"chonk{chonkLevel}", clip.Sheet);
                if (File.Exists(fat)) { path = fat; chonkArt = true; }
            }

            if (!File.Exists(path)) continue;   // art drop is incremental; skip what is not there yet

            lib._clips[clip.Id] = Slice(clip, manifest.Defaults, path, chonkArt);
            if (chonkArt) lib.ChonkArtClips++;
        }
        return lib;
    }

    private static Clip Slice(ClipDef def, ClipDefaults defaults, string path, bool chonkArt = false)
    {
        int fw = def.FrameWidth ?? defaults.FrameWidth;
        int fh = def.FrameHeight ?? defaults.FrameHeight;

        var sheet = new BitmapImage();
        sheet.BeginInit();
        sheet.UriSource = new Uri(path, UriKind.Absolute);
        sheet.CacheOption = BitmapCacheOption.OnLoad;   // read the file now, then release the handle
        sheet.EndInit();
        sheet.Freeze();

        var frames = new BitmapSource[def.Frames];
        var masks = new byte[def.Frames][];

        for (int i = 0; i < def.Frames; i++)
        {
            var crop = new CroppedBitmap(sheet, new System.Windows.Int32Rect(i * fw, 0, fw, fh));
            crop.Freeze();
            frames[i] = crop;
            masks[i] = BuildAlphaMask(crop, fw, fh);
        }

        return new Clip
        {
            Id = def.Id,
            Frames = frames,
            AlphaMasks = masks,
            FrameWidth = fw,
            FrameHeight = fh,
            Fps = def.Fps ?? defaults.Fps,
            Loop = def.Loop ?? defaults.Loop,
            ChonkArt = chonkArt,
            Sprawl = def.Sprawl,
        };
    }

    /// <summary>
    /// One byte of alpha per pixel (~16KB for a 128x128 frame). Cheap, and it is what lets
    /// clicks fall through the transparent corners of the window to whatever is underneath.
    /// </summary>
    private static byte[] BuildAlphaMask(BitmapSource frame, int w, int h)
    {
        var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        bgra.Freeze();

        int stride = w * 4;
        var pixels = new byte[stride * h];
        bgra.CopyPixels(pixels, stride, 0);

        var mask = new byte[w * h];
        for (int i = 0, p = 3; i < mask.Length; i++, p += 4)
            mask[i] = pixels[p];

        return mask;
    }
}
