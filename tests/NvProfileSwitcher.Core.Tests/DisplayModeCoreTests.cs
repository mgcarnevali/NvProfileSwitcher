using System.Text.Json;
using System.Text.Json.Nodes;
using NvProfileSwitcher.Models;
using NvProfileSwitcher.Services;
using Xunit;

namespace NvProfileSwitcher.Core.Tests;

public sealed class DisplayModeCoreTests
{
    private static readonly DisplayModeInfo[] Supported =
    [
        new(2560, 1440, 144), new(2560, 1440, 60),
        new(1920, 1080, 144), new(1920, 1080, 120), new(1920, 1080, 60),
        new(1280, 960, 120), new(1280, 960, 75),
        new(1280, 1024, 60),
    ];

    [Fact]
    public void ChooseModeKeepsTheDesktopRefreshRate() =>
        Assert.Equal(new DisplayModeInfo(1920, 1080, 144),
            DisplayModeCore.ChooseMode(Supported, new DisplayResolution(1920, 1080), new DisplayModeInfo(2560, 1440, 144)));

    [Fact]
    public void ChooseModeFallsBackToTheHighestRefreshRateOfTheResolution() =>
        Assert.Equal(new DisplayModeInfo(1280, 960, 120),
            DisplayModeCore.ChooseMode(Supported, new DisplayResolution(1280, 960), new DisplayModeInfo(2560, 1440, 144)));

    [Fact]
    public void ChooseModeFollowsTheDesktopModeGiven() =>
        Assert.Equal(new DisplayModeInfo(1920, 1080, 60),
            DisplayModeCore.ChooseMode(Supported, new DisplayResolution(1920, 1080), new DisplayModeInfo(2560, 1440, 60)));

    [Fact]
    public void ChooseModeReturnsNullForAnUnsupportedResolution() =>
        Assert.Null(DisplayModeCore.ChooseMode(Supported, new DisplayResolution(800, 600), new DisplayModeInfo(2560, 1440, 144)));

    [Fact]
    public void OfferedModesListEachResolutionOnceLargestFirst()
    {
        var offered = DisplayModeCore.OfferedModes(Supported, new DisplayModeInfo(2560, 1440, 144));

        Assert.Equal(
            [new(2560, 1440, 144), new(1920, 1080, 144), new(1280, 1024, 60), new(1280, 960, 120)],
            offered);
    }

    [Fact]
    public void OfferedModesIgnoreEmptyModes() =>
        Assert.Equal([new DisplayModeInfo(1920, 1080, 60)],
            DisplayModeCore.OfferedModes([new(0, 0, 60), new(1920, 1080, 60)], new DisplayModeInfo(1920, 1080, 60)));

    [Fact]
    public void ResolutionIsValidOnlyWithPositiveSizes()
    {
        Assert.True(new DisplayResolution(1920, 1080).IsValid);
        Assert.False(new DisplayResolution(0, 1080).IsValid);
        Assert.False(new DisplayResolution(1920, -1).IsValid);
    }
}

public sealed class DisplayResolutionConfigurationTests
{
    private static AppConfig Config(DisplayResolution? resolution) => new()
    {
        WindowsProfiles =
        [
            new ProfileDisplaySettings
            {
                Name = "DISPLAY1", DisplayName = "Primary", MonitorId = "MONITOR-1",
                Brightness = 100, Contrast = 100, Gamma = 1.0, DigitalVibrance = 50, Hue = 0
            }
        ],
        ApplicationProfiles =
        [
            new ApplicationProfileConfig
            {
                Id = Guid.Parse("3b393eb8-cc50-455b-996f-12287cb7281e"),
                Name = "Game",
                ExePaths = [@"D:\Games\game.exe"],
                Enabled = true,
                DisplayProfiles =
                [
                    new ProfileDisplaySettings
                    {
                        DisplayName = "Primary", MonitorId = "MONITOR-1",
                        Brightness = 100, Contrast = 105, Gamma = 1.4, DigitalVibrance = 90, Hue = 0,
                        Resolution = resolution
                    }
                ]
            }
        ]
    };

    private static string Serialize(AppConfig config) =>
        JsonSerializer.Serialize(config, NvProfileSwitcherJsonContext.Default.AppConfig);

    [Fact]
    public void AProfileWithoutResolutionIsSavedWithoutTheProperty()
    {
        var json = Serialize(Config(null));

        Assert.DoesNotContain("Resolution", json);
        Assert.True(ConfigurationCore.TryValidateImportedConfiguration(json, out var imported));
        Assert.Null(imported.ApplicationProfiles[0].DisplayProfiles[0].Resolution);
    }

    [Fact]
    public void AResolutionRoundTrips()
    {
        var json = Serialize(Config(new DisplayResolution(1920, 1080)));

        Assert.True(ConfigurationCore.TryValidateImportedConfiguration(json, out var imported));
        Assert.Equal(new DisplayResolution(1920, 1080), imported.ApplicationProfiles[0].DisplayProfiles[0].Resolution);
    }

    [Fact]
    public void ANullResolutionIsAccepted()
    {
        var json = WithResolution("null");

        Assert.True(ConfigurationCore.TryValidateImportedConfiguration(json, out var imported));
        Assert.Null(imported.ApplicationProfiles[0].DisplayProfiles[0].Resolution);
    }

    [Theory]
    [InlineData("\"1920x1080\"")]
    [InlineData("{ \"Width\": 1920 }")]
    [InlineData("{ \"Width\": 0, \"Height\": 1080 }")]
    [InlineData("{ \"Width\": 1920, \"Height\": -1 }")]
    [InlineData("{ \"Width\": \"1920\", \"Height\": 1080 }")]
    [InlineData("{ \"Width\": 1920.5, \"Height\": 1080 }")]
    public void AnInvalidResolutionIsRejected(string value)
    {
        var json = WithResolution(value);

        Assert.False(ConfigurationCore.TryValidateImportedConfiguration(json, out _));
    }

    // The configuration with its resolution replaced by the JSON value given.
    private static string WithResolution(string value)
    {
        var root = JsonNode.Parse(Serialize(Config(new DisplayResolution(1920, 1080))))!;
        var values = Find(root)!;
        values["Resolution"] = JsonNode.Parse(value);
        return root.ToJsonString();

        static JsonObject? Find(JsonNode? node) => node switch
        {
            JsonObject o when o.ContainsKey("Resolution") => o,
            JsonObject o => o.Select(p => Find(p.Value)).FirstOrDefault(found => found is not null),
            JsonArray a => a.Select(Find).FirstOrDefault(found => found is not null),
            _ => null,
        };
    }
}

public sealed class DisplayResolutionEditingTests
{
    private static ProfileListItem Profile(DisplayResolution? resolution, int vibrance = 50)
    {
        var profile = new ProfileListItem("Game");
        profile.DisplayProfiles.Add(new ProfileDisplaySettings
        {
            MonitorId = "MONITOR-1", DisplayName = "Primary",
            Brightness = 100, Contrast = 100, Gamma = 1, DigitalVibrance = vibrance, Hue = 0,
            Resolution = resolution
        });
        return profile;
    }

    [Fact]
    public void ACloneKeepsTheResolution() =>
        Assert.Equal(new DisplayResolution(1920, 1080),
            ProfileEditingCore.CloneProfile(Profile(new DisplayResolution(1920, 1080))).DisplayProfiles[0].Resolution);

    [Fact]
    public void AResolutionChangeIsAProfileChangeButNotAColorChange()
    {
        var a = Profile(null);
        var b = Profile(new DisplayResolution(1920, 1080));

        Assert.False(ProfileEditingCore.ProfilesEqual(a, b));
        Assert.True(ProfileEditingCore.DisplayColorsEqual(a, b));
    }

    [Fact]
    public void CopyingTheColorsLeavesTheResolution()
    {
        var source = Profile(new DisplayResolution(1280, 960), vibrance: 80);
        var destination = Profile(new DisplayResolution(1920, 1080));

        ProfileEditingCore.CopyDisplaySettings(source, destination);

        Assert.Equal(80, destination.DisplayProfiles[0].DigitalVibrance);
        Assert.Equal(new DisplayResolution(1920, 1080), destination.DisplayProfiles[0].Resolution);
    }
}

public sealed class DisplayModeRecoveryStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "NvProfileSwitcher.Tests", Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(directory, "display-modes.json");

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public void EntriesRoundTripWithoutATemporaryFileLeft()
    {
        var store = new DisplayModeRecoveryStore(FilePath);
        DisplayModeRecoveryEntry[] entries =
        [
            new("DEL4321-ABC123", new(2560, 1440, 144), new(1920, 1080, 144), new(1920, 1080, 144)),
            new("GSM5B7F-1", new(1920, 1080, 60), new(1280, 720, 60), null),
        ];

        Assert.True(store.Save(entries));

        Assert.Equal(entries, store.Load());
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void SavingNoEntriesRemovesTheFile()
    {
        var store = new DisplayModeRecoveryStore(FilePath);
        store.Save([new("M", new(1, 1, 60), new(2, 2, 60), null)]);

        Assert.True(store.Save([]));

        Assert.False(store.Exists);
        Assert.Null(store.Load());
    }

    [Fact]
    public void SavingOverAnExistingFileReplacesIt()
    {
        var store = new DisplayModeRecoveryStore(FilePath);
        store.Save([new("A", new(1, 1, 60), new(2, 2, 60), null)]);

        store.Save([new("B", new(3, 3, 60), new(4, 4, 60), null)]);

        Assert.Equal("B", Assert.Single(store.Load()!).MonitorId);
    }

    [Fact]
    public void AFailedSaveReturnsFalseAndKeepsThePreviousFile()
    {
        var store = new DisplayModeRecoveryStore(FilePath);
        store.Save([new("A", new(1, 1, 60), new(2, 2, 60), null)]);
        Directory.CreateDirectory(FilePath + ".tmp");

        Assert.False(store.Save([new("B", new(3, 3, 60), new(4, 4, 60), null)]));

        Assert.Equal("A", Assert.Single(store.Load()!).MonitorId);
    }

    [Fact]
    public void AnUnreadableFileLoadsAsNull()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, "{ \"Monitors\": [ {");

        Assert.Null(new DisplayModeRecoveryStore(FilePath).Load());
    }
}
