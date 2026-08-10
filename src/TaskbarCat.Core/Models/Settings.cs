using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskbarCat.Models;

/// <summary>Persisted cat state. Schema-versioned so a future shape change can migrate.</summary>
public sealed class Settings
{
    /// <summary>Schema the running build writes. Bump when the shape changes, and add a
    /// case to <see cref="SettingsStore.Migrate"/> in the same commit.</summary>
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string Name { get; set; } = "Cat";
    public string ColorPreset { get; set; } = "orange_white";

    public double Fullness { get; set; } = 70;
    public double Cleanliness { get; set; } = 85;
    public double Affection { get; set; } = 50;
    public double Weight { get; set; } = 50;
    public double Tiredness { get; set; } = 20;

    /// <summary>Normalised position along the taskbar, so the cat wakes up where it slept.</summary>
    public double AlongRail { get; set; } = 0.5;

    /// <summary>Drives offline decay on next launch.</summary>
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

    public Needs ToNeeds() => new()
    {
        Fullness = Needs.Clamp(Fullness),
        Cleanliness = Needs.Clamp(Cleanliness),
        Affection = Needs.Clamp(Affection),
        Weight = Needs.Clamp(Weight),
        Tiredness = Needs.Clamp(Tiredness),
    };

    public void CopyFrom(Needs needs, double alongRail)
    {
        Fullness = needs.Fullness;
        Cleanliness = needs.Cleanliness;
        Affection = needs.Affection;
        Weight = needs.Weight;
        Tiredness = needs.Tiredness;
        AlongRail = alongRail;
        LastSeenUtc = DateTime.UtcNow;
    }
}

/// <summary>
/// Loads and saves <see cref="Settings"/> as JSON under %APPDATA%.
///
/// Writes are atomic (temp file then replace): the app is killed by logoff, Task Manager
/// and crashes far more often than it is closed cleanly, and a half-written settings file
/// would reset the user's cat.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public SettingsStore(string? path = null)
    {
        Path = path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TaskbarCat", "settings.json");
    }

    public string Path { get; }

    public Settings Load()
    {
        try
        {
            if (!File.Exists(Path)) return new Settings();
            var json = File.ReadAllText(Path);
            return Migrate(JsonSerializer.Deserialize<Settings>(json, Options) ?? new Settings());
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable file must not stop the cat from existing.
            return new Settings();
        }
    }

    /// <summary>
    /// Brings a loaded file up to <see cref="Settings.CurrentSchemaVersion"/>.
    ///
    /// Two failure modes this exists to stop. An OLDER file must be upgraded field by field,
    /// not discarded — the user's cat is the only thing in here worth keeping. A NEWER file
    /// (they ran a beta, then rolled back) must be read for what it does understand and left
    /// alone otherwise; the alternative is silently resetting a cat someone has fed for weeks.
    ///
    /// Values are clamped regardless of version: the file is plain JSON in %APPDATA% and
    /// people edit it.
    /// </summary>
    internal static Settings Migrate(Settings s)
    {
        switch (s.SchemaVersion)
        {
            case <= 0:
                // Pre-versioning or hand-stripped. Field names have not changed, so what
                // deserialised is usable as-is; defaults filled the rest.
                s.SchemaVersion = Settings.CurrentSchemaVersion;
                break;

            case Settings.CurrentSchemaVersion:
                break;

            // case 1: migrate v1 -> v2 here when the shape next changes, then fall through.

            default:
                // From the future. Keep the parsed values, keep the higher version number so a
                // re-run of the newer build still recognises its own file, and write nothing new.
                break;
        }

        s.Fullness = Needs.Clamp(s.Fullness);
        s.Cleanliness = Needs.Clamp(s.Cleanliness);
        s.Affection = Needs.Clamp(s.Affection);
        s.Weight = Needs.Clamp(s.Weight);
        s.Tiredness = Needs.Clamp(s.Tiredness);
        s.AlongRail = Math.Clamp(s.AlongRail, 0, 1);
        if (string.IsNullOrWhiteSpace(s.ColorPreset)) s.ColorPreset = "orange_white";
        if (string.IsNullOrWhiteSpace(s.Name)) s.Name = "Cat";

        return s;
    }

    public void Save(Settings settings)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(Path)!;
            Directory.CreateDirectory(dir);

            var tmp = Path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));

            if (File.Exists(Path)) File.Replace(tmp, Path, null);
            else File.Move(tmp, Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing a save is survivable; crashing the pet over it is not.
        }
    }
}
