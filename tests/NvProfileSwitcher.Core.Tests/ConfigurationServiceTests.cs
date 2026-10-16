using System.Text;
using System.Text.Json.Nodes;
using Xunit;
using static NvProfileSwitcher.Core.Tests.ConfigFixtures;

namespace NvProfileSwitcher.Core.Tests;

public sealed class ConfigurationServiceTests
{
    [Fact]
    public void CanonicalConfigurationRoundTripsByteForByte()
    {
        using var session = new ConfigSession(WithPlatformNewLines(Canonical));
        var original = File.ReadAllBytes(session.ConfigPath);

        session.Service.Load();
        Assert.True(session.Service.Save());

        Assert.Equal(original, File.ReadAllBytes(session.ConfigPath));
        Assert.Equal(Encoding.UTF8.GetBytes(WithPlatformNewLines(Canonical)), original);
    }

    [Fact]
    public void LoadMapsEveryField()
    {
        using var session = new ConfigSession(Canonical);
        session.Service.Load();

        var profiles = session.Profiles.Profiles;
        Assert.Equal(["Windows", "Café & Games", "Tarkov"], profiles.Select(p => p.Name));
        Assert.True(profiles[0].IsWindowsProfile);
        Assert.Equal(Guid.Empty, profiles[0].Id);
        Assert.Equal(["Display 1", "Display 2"], profiles[0].DisplayProfiles.Select(d => d.Name));

        var game = profiles[1];
        Assert.Equal(Guid.Parse(GameId), game.Id);
        Assert.True(game.Enabled);
        Assert.Equal(628, game.Hotkey);
        Assert.Equal([@"C:\Games\Café\game+.exe"], game.ExecutablePaths);
        Assert.Equal(2, game.DisplayProfiles.Count);
        Assert.Null(game.DisplayProfiles[0].Name);
        Assert.Equal(1.15, game.DisplayProfiles[0].Gamma);
        Assert.Equal(355, game.DisplayProfiles[1].Hue);

        var tarkov = profiles[2];
        Assert.False(tarkov.Enabled);
        Assert.Equal(2, tarkov.ExecutablePaths.Count);
        Assert.Empty(tarkov.DisplayProfiles);

        var settings = session.Settings;
        Assert.True(settings.StartWithWindows);
        Assert.True(settings.StartMinimized);
        Assert.True(settings.MinimizeToTray);
        Assert.False(settings.AlwaysOnTop);
        Assert.True(settings.CheckForUpdates);
        Assert.True(settings.RememberGlobalSwitching);
        Assert.True(settings.RememberOverride);
        Assert.Equal([1648, 1649, 1650, 1651],
            [settings.ShowHideHotkey, settings.WindowsOverrideHotkey, settings.ResumeAutomaticHotkey, settings.GlobalSwitchingHotkey]);

        Assert.True(session.Switching.GlobalPaused);
        Assert.Equal(OverrideMode.Profile, session.Switching.OverrideMode);
        Assert.Equal(0, session.Switching.OverrideProfileIndex);
        Assert.Equal("Windows", session.Switching.ActiveProfileName);
    }

    [Fact]
    public void DevelopmentBuildsNeverCheckForUpdates()
    {
        using var session = new ConfigSession(Canonical, isDevelopmentBuild: true);
        session.Service.Load();

        Assert.False(session.Settings.CheckForUpdates);
        Assert.Equal(["$.CheckForUpdates: true -> false"], Differences(Canonical, session.SaveAndRead()));
    }

    // Modifying only display values and saving must leave every other field untouched:
    // profiles, executables, hotkeys, settings and switching state.
    [Fact]
    public void ChangingOnlyDisplayValuesPreservesTheRestOfTheConfiguration()
    {
        using var session = new ConfigSession(Canonical);
        session.Service.Load();

        session.Profiles.Windows!.DisplayProfiles[1].Brightness = 110;
        session.Profiles.ApplicationProfiles[0].DisplayProfiles[0].DigitalVibrance = 90;

        Assert.Equal(
            [
                "$.Windows Profiles[1].Brightness: 98 -> 110",
                "$.Application Profiles[0].Display Profiles[0].DigitalVibrance: 80 -> 90"
            ],
            Differences(Canonical, session.SaveAndRead()));
    }

    [Fact]
    public void GlobalSwitchingIsSavedEnabledWhenItIsNotRemembered()
    {
        var input = Variant(c => c["RememberGlobalSwitching"] = false);
        using var session = new ConfigSession(input);
        session.Service.Load();

        Assert.False(session.Switching.GlobalPaused);
        Assert.Equal(["$.GlobalSwitchingEnabled: false -> true"], Differences(input, session.SaveAndRead()));
    }

    [Fact]
    public void OverrideIsSavedAutomaticWhenItIsNotRemembered()
    {
        var input = Variant(c => c["RememberOverride"] = false);
        using var session = new ConfigSession(input);
        session.Service.Load();

        Assert.Equal(OverrideMode.Automatic, session.Switching.OverrideMode);
        Assert.Equal(
            ["$.OverrideMode: \"Profile\" -> \"Automatic\"", $"$.OverrideProfileId: \"{GameId}\" -> null"],
            Differences(input, session.SaveAndRead()));
    }

    [Fact]
    public void OverrideOfADisabledProfileIsNotRestored()
    {
        var input = Variant(c => c["OverrideProfileId"] = TarkovId);
        using var session = new ConfigSession(input);
        session.Service.Load();

        Assert.Equal(OverrideMode.Automatic, session.Switching.OverrideMode);
        Assert.Equal(-1, session.Switching.OverrideProfileIndex);
        Assert.Equal(
            ["$.OverrideMode: \"Profile\" -> \"Automatic\"", $"$.OverrideProfileId: \"{TarkovId}\" -> null"],
            Differences(input, session.SaveAndRead()));
    }

    [Fact]
    public void ExecutablePathsAreTrimmedAndDeduplicated()
    {
        var input = Variant(c => c["Application Profiles"]![1]!["Exe Paths"] =
            new JsonArray(@" D:\Games\EscapeFromTarkov.exe ", @"d:\games\ESCAPEFROMTARKOV.EXE", @"D:\Games\Arena.exe"));
        using var session = new ConfigSession(input);
        session.Service.Load();

        Assert.Equal([@"D:\Games\EscapeFromTarkov.exe", @"D:\Games\Arena.exe"], session.Profiles.ApplicationProfiles[1].ExecutablePaths);
        var saved = Node(session.SaveAndRead());
        Assert.Equal(
            [@"D:\Games\EscapeFromTarkov.exe", @"D:\Games\Arena.exe"],
            saved["Application Profiles"]![1]!["Exe Paths"]!.AsArray().Select(p => p!.GetValue<string>()));
    }

    [Fact]
    public void EmptyObjectLoadsTheConfigurationDefaults()
    {
        using var session = new ConfigSession("{}");
        session.Service.Load();

        Assert.Single(session.Profiles.Profiles);
        Assert.False(session.Switching.GlobalPaused);
        var saved = Node(session.SaveAndRead());
        Assert.True(saved["GlobalSwitchingEnabled"]!.GetValue<bool>());
        Assert.Equal("Automatic", saved["OverrideMode"]!.GetValue<string>());
        Assert.Null(saved["OverrideProfileId"]);
        Assert.Empty(saved["Windows Profiles"]!.AsArray());
        Assert.Empty(saved["Application Profiles"]!.AsArray());
    }

    [Fact]
    public void MissingFileStartsWithDefaultsAndCreatesNothing()
    {
        using var session = new ConfigSession();
        session.Service.Load();

        AssertSessionDefaults(session);
        Assert.False(File.Exists(session.ConfigPath));
    }

    public static TheoryData<string> UnreadableConfigurations() => new()
    {
        "{ not json",
        "null",
        "",
        Variant(c => c["Application Profiles"]![0]!["Exe Paths"] = null),
        Variant(c => c["Application Profiles"]![0]!["Display Profiles"] = null),
        Variant(c => c["Windows Profiles"] = null),
        Variant(c => c["Application Profiles"] = null)
    };

    // As in the original load, any failure discards the whole configuration and the
    // session starts with defaults, leaving the file untouched.
    [Theory]
    [MemberData(nameof(UnreadableConfigurations))]
    public void UnreadableConfigurationStartsWithDefaultsAndIsNotModified(string json)
    {
        using var session = new ConfigSession(json);
        var original = File.ReadAllBytes(session.ConfigPath);

        session.Service.Load();

        AssertSessionDefaults(session);
        Assert.Equal(original, File.ReadAllBytes(session.ConfigPath));
    }

    [Fact]
    public void LoadNeverWritesTheConfiguration()
    {
        using var session = new ConfigSession(Canonical);
        var original = File.ReadAllBytes(session.ConfigPath);
        var lastWrite = File.GetLastWriteTimeUtc(session.ConfigPath);

        session.Service.Load();

        Assert.Equal(original, File.ReadAllBytes(session.ConfigPath));
        Assert.Equal(lastWrite, File.GetLastWriteTimeUtc(session.ConfigPath));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(session.ConfigPath)!));
    }

    [Fact]
    public void SaveCreatesTheDirectoryAndLeavesNoTemporaryFile()
    {
        using var session = new ConfigSession(createDirectory: false);
        session.Service.Load();

        var saved = session.SaveAndRead();

        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(session.ConfigPath)!));
        Assert.Empty(Node(saved)["Windows Profiles"]!.AsArray());
    }

    [Fact]
    public void SaveReplacesAnExistingConfiguration()
    {
        using var session = new ConfigSession(Canonical);
        session.Service.Load();
        session.Settings.AlwaysOnTop = true;

        Assert.Equal(["$.AlwaysOnTop: false -> true"], Differences(Canonical, session.SaveAndRead()));
    }

    private static void AssertSessionDefaults(ConfigSession session)
    {
        var windows = Assert.Single(session.Profiles.Profiles);
        Assert.True(windows.IsWindowsProfile);
        Assert.Equal("Windows", windows.Name);
        Assert.Empty(windows.DisplayProfiles);

        var settings = session.Settings;
        Assert.False(settings.StartWithWindows || settings.StartMinimized || settings.MinimizeToTray ||
                     settings.AlwaysOnTop || settings.CheckForUpdates ||
                     settings.RememberGlobalSwitching || settings.RememberOverride);
        Assert.Equal([0, 0, 0, 0],
            [settings.ShowHideHotkey, settings.WindowsOverrideHotkey, settings.ResumeAutomaticHotkey, settings.GlobalSwitchingHotkey]);

        Assert.False(session.Switching.GlobalPaused);
        Assert.Equal(OverrideMode.Automatic, session.Switching.OverrideMode);
        Assert.Equal(-1, session.Switching.OverrideProfileIndex);
    }

    // The unsaved new profile is never written, wherever it is in the list; the profile
    // override still names the profile its index refers to in the whole list.
    [Fact]
    public void TheUnsavedNewProfileIsNotSavedAndKeepsTheOverrideOnItsProfile()
    {
        using var session = new ConfigSession(Canonical);
        session.Service.Load();
        var tarkov = session.Profiles.ApplicationProfiles[1];
        var unsaved = new ProfileListItem("New Profile") { Enabled = true };
        session.Profiles.AddUnsaved(unsaved);
        session.Profiles.Move(3, 2);
        session.Settings.RememberOverride = true;
        session.Switching.OverrideMode = OverrideMode.Profile;
        session.Switching.OverrideProfileIndex = 2;

        var saved = Node(session.SaveAndRead());

        Assert.Equal(
            [session.Profiles.ApplicationProfiles[0].Name, "Tarkov"],
            saved["Application Profiles"]!.AsArray().Select(p => (string)p!["Name"]!));
        Assert.Equal(tarkov.Id.ToString(), (string)saved["OverrideProfileId"]!);
        Assert.Equal("Profile", (string)saved["OverrideMode"]!);
    }

    [Fact]
    public void TheNewProfileIsSavedOnceItIsNoLongerUnsaved()
    {
        using var session = new ConfigSession(Canonical);
        session.Service.Load();
        var added = new ProfileListItem("Racing") { Enabled = true };
        session.Profiles.AddUnsaved(added);
        Assert.DoesNotContain("Racing", session.SaveAndRead());

        session.Profiles.MarkSaved(added);

        Assert.Contains("\"Racing\"", session.SaveAndRead());
    }

    // Values without a monitor id belong to no display: they are dropped on load and the next
    // save no longer writes them.
    [Fact]
    public void ValuesWithoutMonitorIdAreDroppedOnLoad()
    {
        var blank = """{"DisplayName":"Projector","MonitorId":"","Brightness":100,"Contrast":100,"Gamma":1,"DigitalVibrance":50,"Hue":0}""";
        const string list = "\"Display Profiles\": [";
        var json = Canonical.Insert(Canonical.IndexOf(list, StringComparison.Ordinal) + list.Length, blank + ",");
        using var session = new ConfigSession(json);

        session.Service.Load();
        Assert.All(session.Profiles.Profiles, p => Assert.DoesNotContain(p.DisplayProfiles, v => string.IsNullOrWhiteSpace(v.MonitorId)));

        Assert.True(session.Service.Save());
        Assert.DoesNotContain("\"MonitorId\": \"\"", File.ReadAllText(session.ConfigPath));
    }
}
