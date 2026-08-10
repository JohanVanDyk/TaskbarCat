using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskbarCat.Models;

/// <summary>Persisted cat state. Schema-versioned so a future shape change can migrate.</summary>
public sealed class Settings
{
    public int SchemaVersion { get; set; } = 1;

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
            return JsonSerializer.Deserialize<Settings>(json, Options) ?? new Settings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable file must not stop the cat from existing.
            return new Settings();
        }
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
