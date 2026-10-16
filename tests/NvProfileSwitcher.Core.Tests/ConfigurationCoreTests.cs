using Xunit;
using System.Text.Json;

namespace NvProfileSwitcher.Core.Tests;

public sealed class ConfigurationCoreTests
{
    private static AppConfig CompleteConfig() => new()
    {
        StartWithWindows = true,
        StartMinimized = true,
        MinimizeToTray = true,
        AlwaysOnTop = true,
        CheckForUpdates = false,
        RememberGlobalSwitching = true,
        RememberOverride = true,
        GlobalSwitchingEnabled = false,
        OverrideMode = OverrideMode.Profile,
        OverrideProfileId = Guid.Parse("3b393eb8-cc50-455b-996f-12287cb7281e"),
        ShowHideHotkey = 0x024E,
        WindowsOverrideHotkey = 0x0257,
        ResumeAutomaticHotkey = 0x0252,
        GlobalSwitchingHotkey = 0x0247,
        WindowsProfiles =
        [
            new ProfileDisplaySettings
            {
                Name = "DISPLAY1", DisplayName = "Primary", MonitorId = "MONITOR-1",
                Brightness = 100, Contrast = 100, Gamma = 1.0, DigitalVibrance = 60, Hue = 0
            }
        ],
        ApplicationProfiles =
        [
            new ApplicationProfileConfig
            {
                Id = Guid.Parse("3b393eb8-cc50-455b-996f-12287cb7281e"),
                Name = "Tarkov",
                ExePaths = [@"D:\Games\EscapeFromTarkov.exe", @"D:\Games\BsgLauncher.exe"],
                Enabled = true, Hotkey = 0x0254,
                DisplayProfiles =
                [
                    new ProfileDisplaySettings
                    {
                        DisplayName = "Primary", MonitorId = "MONITOR-1",
                        Brightness = 100, Contrast = 105, Gamma = 1.4, DigitalVibrance = 90, Hue = 0
                    }
                ]
            }
        ]
    };

    [Fact]
    public void CompleteConfigurationRoundTripsAndValidates()
    {
        var source = CompleteConfig();
        var json = JsonSerializer.Serialize(source);
        Assert.DoesNotContain("\"ExePath\"", json);
        Assert.True(ConfigurationCore.TryValidateImportedConfiguration(json, out var imported));
        Assert.Equal(source.StartWithWindows, imported.StartWithWindows);
        Assert.Equal(source.StartMinimized, imported.StartMinimized);
        Assert.Equal(source.MinimizeToTray, imported.MinimizeToTray);
        Assert.Equal(source.AlwaysOnTop, imported.AlwaysOnTop);
        Assert.Equal(source.CheckForUpdates, imported.CheckForUpdates);
        Assert.Equal(source.RememberGlobalSwitching, imported.RememberGlobalSwitching);
        Assert.Equal(source.RememberOverride, imported.RememberOverride);
        Assert.Equal(source.GlobalSwitchingEnabled, imported.GlobalSwitchingEnabled);
        Assert.Equal(source.OverrideMode, imported.OverrideMode);
        Assert.Equal(source.OverrideProfileId, imported.OverrideProfileId);
        Assert.Equal(source.ShowHideHotkey, imported.ShowHideHotkey);
        Assert.Equal(source.WindowsOverrideHotkey, imported.WindowsOverrideHotkey);
        Assert.Equal(source.ResumeAutomaticHotkey, imported.ResumeAutomaticHotkey);
        Assert.Equal(source.GlobalSwitchingHotkey, imported.GlobalSwitchingHotkey);
        Assert.Single(imported.WindowsProfiles);
        Assert.Single(imported.ApplicationProfiles);
        Assert.Equal("MONITOR-1", imported.WindowsProfiles[0].MonitorId);
        Assert.Equal(source.ApplicationProfiles[0].Id, imported.ApplicationProfiles[0].Id);
        Assert.Equal("Tarkov", imported.ApplicationProfiles[0].Name);
        Assert.Equal(2, imported.ApplicationProfiles[0].ExePaths.Count);
        Assert.Equal(@"D:\Games\BsgLauncher.exe", imported.ApplicationProfiles[0].ExePaths[1]);
        Assert.Equal(90, imported.ApplicationProfiles[0].DisplayProfiles[0].DigitalVibrance);
    }

    [Fact]
    public void SharedMonitorIdentitiesAreOptionalAndRoundTrip()
    {
        var config = CompleteConfig();
        Assert.True(ConfigurationCore.TryValidateImportedConfiguration(JsonSerializer.Serialize(config), out var withoutShared));
        Assert.Null(withoutShared.NonUniqueMonitorIds);

        config.NonUniqueMonitorIds = ["SAM0F12-1"];
        Assert.True(ConfigurationCore.TryValidateImportedConfiguration(JsonSerializer.Serialize(config), out var withShared));
        Assert.Equal(["SAM0F12-1"], withShared.NonUniqueMonitorIds);
    }

    [Theory]
    [InlineData("[1]")]
    [InlineData("[\"  \"]")]
    [InlineData("\"SAM0F12-1\"")]
    public void InvalidSharedMonitorIdentitiesAreRejected(string value)
    {
        var json = JsonSerializer.Serialize(CompleteConfig());
        json = json.TrimEnd().TrimEnd('}') + ",\"Non-unique Monitor Ids\":" + value + "}";
        Assert.False(ConfigurationCore.TryValidateImportedConfiguration(json, out _));
    }

    [Fact]
    public void MissingProfileIdIsRejected()
    {
        var json = JsonSerializer.Serialize(CompleteConfig());
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement.Clone();
        var profile = root.GetProperty("Application Profiles")[0];
        var profileValues = profile.EnumerateObject()
            .Where(p => p.Name != nameof(ApplicationProfileConfig.Id))
            .ToDictionary(p => p.Name, p => p.Value.Clone());
        var rootValues = root.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Name == "Application Profiles"
                ? JsonSerializer.SerializeToElement(new[] { profileValues })
                : p.Value.Clone());

        Assert.False(ConfigurationCore.TryValidateImportedConfiguration(JsonSerializer.Serialize(rootValues), out _));
    }

    [Fact]
    public void EmptyProfileIdIsRejected()
    {
        var config = CompleteConfig();
        config.ApplicationProfiles[0].Id = Guid.Empty;
        Assert.False(ConfigurationCore.TryValidateImportedConfiguration(JsonSerializer.Serialize(config), out _));
    }

    [Fact]
    public void DuplicateProfileIdsAreRejected()
    {
        var config = CompleteConfig();
        var duplicate = new ApplicationProfileConfig
        {
            Id = config.ApplicationProfiles[0].Id,
            Name = "Arena",
            ExePaths = [@"D:\Games\Arena.exe"],
            Enabled = true,
            Hotkey = 0,
            DisplayProfiles = [ProfileEditingCore.CloneDisplaySettings(config.ApplicationProfiles[0].DisplayProfiles[0])]
        };
        config.ApplicationProfiles.Add(duplicate);

        Assert.False(ConfigurationCore.TryValidateImportedConfiguration(JsonSerializer.Serialize(config), out _));
    }

    [Fact]
    public void InvalidJsonIsRejected() =>
        Assert.False(ConfigurationCore.TryValidateImportedConfiguration("{ not-json", out _));

    [Fact]
    public void MissingRequiredGlobalSettingIsRejected()
    {
        var json = JsonSerializer.Serialize(CompleteConfig());
        using var document = JsonDocument.Parse(json);
        var values = document.RootElement.EnumerateObject()
            .Where(p => p.Name != nameof(AppConfig.CheckForUpdates))
            .ToDictionary(p => p.Name, p => p.Value.Clone());
        var incomplete = JsonSerializer.Serialize(values);
        Assert.False(ConfigurationCore.TryValidateImportedConfiguration(incomplete, out _));
    }

    [Fact]
    public void MissingGlobalSwitchingHotkeyIsRejected()
    {
        var json = JsonSerializer.Serialize(CompleteConfig());
        using var document = JsonDocument.Parse(json);
        var values = document.RootElement.EnumerateObject()
            .Where(p => p.Name != nameof(AppConfig.GlobalSwitchingHotkey))
            .ToDictionary(p => p.Name, p => p.Value.Clone());

        Assert.False(ConfigurationCore.TryValidateImportedConfiguration(JsonSerializer.Serialize(values), out _));
    }

    [Fact]
    public void InvalidDisplayValueTypeIsRejected()
    {
        var json = JsonSerializer.Serialize(CompleteConfig());
        json = json.Replace("\"DigitalVibrance\":90", "\"DigitalVibrance\":\"90\"");
        Assert.False(ConfigurationCore.TryValidateImportedConfiguration(json, out _));
    }
    [Fact]
    public void MissingExecutablePathsIsRejected()
    {
        var json = JsonSerializer.Serialize(CompleteConfig());
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement.Clone();
        var profile = root.GetProperty("Application Profiles")[0];
        var profileValues = profile.EnumerateObject()
            .Where(p => p.Name != "Exe Paths")
            .ToDictionary(p => p.Name, p => p.Value.Clone());
        var rootValues = root.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Name == "Application Profiles"
                ? JsonSerializer.SerializeToElement(new[] { profileValues })
                : p.Value.Clone());

        Assert.False(ConfigurationCore.TryValidateImportedConfiguration(JsonSerializer.Serialize(rootValues), out _));
    }

    [Fact]
    public void MissingPersistenceSettingIsRejected()
    {
        var json = JsonSerializer.Serialize(CompleteConfig());
        using var document = JsonDocument.Parse(json);
        var values = document.RootElement.EnumerateObject()
            .Where(p => p.Name != nameof(AppConfig.RememberOverride))
            .ToDictionary(p => p.Name, p => p.Value.Clone());

        Assert.False(ConfigurationCore.TryValidateImportedConfiguration(JsonSerializer.Serialize(values), out _));
    }

    [Fact]
    public void OverrideModeIsSerializedByName()
    {
        var json = JsonSerializer.Serialize(CompleteConfig());
        Assert.Contains("\"OverrideMode\":\"Profile\"", json);
        Assert.True(ConfigurationCore.TryValidateImportedConfiguration(json, out var imported));
        Assert.Equal(OverrideMode.Profile, imported.OverrideMode);
    }

}
