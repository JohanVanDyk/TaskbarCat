using TaskbarCat.Models;
using Xunit;

namespace TaskbarCat.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "taskbarcat-tests", Guid.NewGuid().ToString("N"));

    private string File1 => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void RoundTrips_Needs_AndPosition()
    {
        var store = new SettingsStore(File1);
        var settings = new Settings { Name = "Mango" };
        settings.CopyFrom(new Needs { Fullness = 42, Affection = 88, Weight = 61 }, alongRail: 0.75);

        store.Save(settings);
        var loaded = new SettingsStore(File1).Load();

        Assert.Equal("Mango", loaded.Name);
        Assert.Equal(0.75, loaded.AlongRail, 3);
        Assert.Equal(42, loaded.ToNeeds().Fullness, 3);
        Assert.Equal(88, loaded.ToNeeds().Affection, 3);
    }

    [Fact]
    public void MissingFile_YieldsDefaults_NotACrash()
    {
        var loaded = new SettingsStore(Path.Combine(_dir, "nope.json")).Load();
        Assert.Equal(70, loaded.ToNeeds().Fullness, 3);
    }

    [Fact]
    public void CorruptFile_YieldsDefaults_NotACrash()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File1, "{ this is not json");

        var loaded = new SettingsStore(File1).Load();

        Assert.Equal(70, loaded.ToNeeds().Fullness, 3);
    }

    [Fact]
    public void FileFromAFutureBuild_KeepsTheCat_InsteadOfResettingIt()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File1, """
            { "SchemaVersion": 99, "Name": "Mango", "Fullness": 12, "AlongRail": 0.9, "UnknownFutureField": true }
            """);

        var loaded = new SettingsStore(File1).Load();

        Assert.Equal("Mango", loaded.Name);
        Assert.Equal(12, loaded.ToNeeds().Fullness, 3);
        Assert.Equal(99, loaded.SchemaVersion);
    }

    [Fact]
    public void FileWithNoVersion_IsAdoptedAtTheCurrentSchema()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File1, """{ "Name": "Mango", "Fullness": 33 }""");

        var loaded = new SettingsStore(File1).Load();

        Assert.Equal(Settings.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(33, loaded.ToNeeds().Fullness, 3);
    }

    [Fact]
    public void HandEditedOutOfRangeValues_AreClamped()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File1, """{ "Fullness": 9999, "Tiredness": -50, "AlongRail": 4.2, "Name": "  " }""");

        var loaded = new SettingsStore(File1).Load();

        Assert.Equal(100, loaded.ToNeeds().Fullness, 3);
        Assert.Equal(0, loaded.ToNeeds().Tiredness, 3);
        Assert.Equal(1.0, loaded.AlongRail, 3);
        Assert.Equal("Cat", loaded.Name);
    }

    [Fact]
    public void Save_OverExistingFile_Replaces_NotAppends()
    {
        var store = new SettingsStore(File1);
        store.Save(new Settings { Name = "First" });
        store.Save(new Settings { Name = "Second" });

        Assert.Equal("Second", store.Load().Name);
        Assert.False(File.Exists(File1 + ".tmp"));
    }
}
