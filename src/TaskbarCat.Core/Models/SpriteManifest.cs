using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TaskbarCat.Models;

/// <summary>
/// Deserialised form of Assets/sprites.json. Pure data — no WPF types, so the
/// manifest can be validated in tests without a UI thread.
/// </summary>
public sealed class SpriteManifest
{
    public int Version { get; init; } = 1;
    public ClipDefaults Defaults { get; init; } = new();
    public IReadOnlyList<ColorPreset> ColorPresets { get; init; } = [];
    public IReadOnlyList<ClipDef> Clips { get; init; } = [];
    public IReadOnlyList<ClipDef> Effects { get; init; } = [];

    public SpriteSet? Faces { get; init; }
    public SpriteSet? Bubbles { get; init; }
    public SpriteSet? UiIcons { get; init; }
    public SpriteSet? StatusIcons { get; init; }
    public SpriteSet? Accessories { get; init; }
    public SpriteSet? Props { get; init; }
}

public sealed class ClipDefaults
{
    public int FrameWidth { get; init; } = 128;
    public int FrameHeight { get; init; } = 128;
    public int Fps { get; init; } = 12;
    public bool Loop { get; init; } = true;
    public bool Interruptible { get; init; } = true;
}

public sealed class ColorPreset
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string SheetDir { get; init; }

    [JsonPropertyName("default")]
    public bool IsDefault { get; init; }

    /// <summary>Free text — used to mark presets derived by tools/make_preset.py rather than drawn.</summary>
    public string? Notes { get; init; }
}

/// <summary>A single animation strip. Nulls fall back to <see cref="ClipDefaults"/>.</summary>
public sealed class ClipDef
{
    public required string Id { get; init; }
    public required string Sheet { get; init; }
    public required int Frames { get; init; }

    public int? FrameWidth { get; init; }
    public int? FrameHeight { get; init; }
    public int? Fps { get; init; }
    public bool? Loop { get; init; }
    public bool? Interruptible { get; init; }

    public MotionDef? Motion { get; init; }
    public AttachmentDef? Bubble { get; init; }
    public AttachmentDef? Prop { get; init; }
    public string? Notes { get; init; }

    /// <summary>
    /// True when the cat is drawn stretched out along the ground — running, walking, sleeping,
    /// pouncing — rather than sitting or standing.
    ///
    /// It only affects how an overfed cat with no drawn art is stretched, and it matters because
    /// widening a sprawled pose makes the cat LONGER, not fatter. See <see cref="ChonkVisuals"/>.
    /// Measured, not judged: mean opaque bbox width over height per clip, sprawl above ~1.15.
    /// </summary>
    public bool Sprawl { get; init; }
}

/// <summary>Per-frame translation baked into a walk clip, so gait and travel stay in sync.</summary>
public sealed class MotionDef
{
    public required double PixelsPerFrame { get; init; }
    public required string Direction { get; init; }
}

public sealed class AttachmentDef
{
    public string? Id { get; init; }
    public string? IconId { get; init; }
    public int OffsetX { get; init; }
    public int OffsetY { get; init; }
}

/// <summary>A uniform grid of same-sized images addressed by name (faces, icons, props).</summary>
public sealed class SpriteSet
{
    public required string Sheet { get; init; }
    public required int FrameWidth { get; init; }
    public required int FrameHeight { get; init; }
    public IReadOnlyList<string> Ids { get; init; } = [];
}
