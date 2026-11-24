using System.Text.Json;
using System.Text.Json.Nodes;
using NvProfileSwitcher.Services;
using Xunit;
using static NvProfileSwitcher.Core.Tests.FakeDisplayBackend;
using static NvProfileSwitcher.Core.Tests.SwitchingFixture;

namespace NvProfileSwitcher.Core.Tests;

// The application without the NVIDIA API, or with displays it does not control: the colors of
// those displays are never written, edited or made up, and the values saved for them are kept.
public sealed class GpuIndependenceTests
{
    private const string Panel = "BOE0A1B-77";

    private static ConnectedDisplay WithoutColors(string monitorId, string name, int number = 1) =>
        Display(monitorId, name, number) with { Nvidia = null };

    private static List<ProfileListItem> Profiles(params ProfileListItem[] applications) =>
        [new ProfileListItem("Windows", isWindowsProfile: true), .. applications];

    private static DisplaySyncResult Sync(List<ProfileListItem> profiles, params ConnectedDisplay[] displays) =>
        DisplayProfileSyncCore.Synchronize(profiles, displays, new HashSet<string>(), 0, _ => 70, _ => 15);

    private static (double, double, double, int, int) Colors(ProfileDisplaySettings values) =>
        (values.Brightness, values.Contrast, values.Gamma, values.DigitalVibrance, values.Hue);

    // ---- Synchronization -------------------------------------------------------------------

    [Fact]
    public void ADisplayWithoutColorsGetsNoMadeUpValues()
    {
        var game = new ProfileListItem("Game");
        var profiles = Profiles(game);

        var result = Sync(profiles, WithoutColors(Dell, "DELL U2720Q"));

        Assert.False(result.Changed);
        Assert.Empty(profiles[0].DisplayProfiles);
        Assert.Empty(game.DisplayProfiles);
    }

    [Fact]
    public void SavedValuesOfADisplayWithoutColorsAreKeptAndOnlyRenamed()
    {
        var game = new ProfileListItem("Game");
        game.DisplayProfiles.Add(new ProfileDisplaySettings { DisplayName = "Old", MonitorId = Dell, DigitalVibrance = 80, Gamma = 1.2 });
        var profiles = Profiles(game);
        profiles[0].DisplayProfiles.Add(new ProfileDisplaySettings { DisplayName = "Old", MonitorId = Dell, DigitalVibrance = 65 });

        Assert.True(Sync(profiles, WithoutColors(Dell, "DELL U2720Q")).Changed);

        Assert.Equal((100d, 100d, 1.2, 80, 0), Colors(game.DisplayProfiles.Single()));
        Assert.Equal(65, profiles[0].DisplayProfiles.Single().DigitalVibrance);
        Assert.All(profiles.SelectMany(p => p.DisplayProfiles), v => Assert.Equal("DELL U2720Q", v.DisplayName));
    }

    // The colors of a monitor first read when they become available are the ones it keeps; values
    // that only held its resolution take them, so the made-up ones are never applied.
    [Fact]
    public void PendingValuesTakeTheFirstColorsReadAndKeepTheirResolution()
    {
        var game = new ProfileListItem("Game");
        game.DisplayProfiles.Add(new ProfileDisplaySettings
        {
            DisplayName = "DELL", MonitorId = Dell, ColorsPending = true, Resolution = new DisplayResolution(1920, 1080)
        });
        var profiles = Profiles(game);

        Sync(profiles, WithoutColors(Dell, "DELL U2720Q"));
        Assert.True(game.DisplayProfiles.Single().ColorsPending);

        Assert.True(Sync(profiles, Display(Dell, "DELL U2720Q")).Changed);

        var windows = profiles[0].DisplayProfiles.Single();
        var values = game.DisplayProfiles.Single();
        Assert.Equal((100d, 100d, 1d, 70, 15), Colors(windows));
        Assert.Equal(Colors(windows), Colors(values));
        Assert.False(values.ColorsPending);
        Assert.Equal(new DisplayResolution(1920, 1080), values.Resolution);
    }

    // A monitor with Windows values (its colors were read before, on another adapter now) gives
    // them to pending values right away.
    [Fact]
    public void PendingValuesTakeTheWindowsValuesTheMonitorAlreadyHas()
    {
        var game = new ProfileListItem("Game");
        game.DisplayProfiles.Add(new ProfileDisplaySettings { DisplayName = "DELL", MonitorId = Dell, ColorsPending = true });
        var profiles = Profiles(game);
        profiles[0].DisplayProfiles.Add(new ProfileDisplaySettings { DisplayName = "DELL", MonitorId = Dell, DigitalVibrance = 65, Brightness = 105 });

        Sync(profiles, WithoutColors(Dell, "DELL"));

        Assert.Equal((105d, 100d, 1d, 65, 0), Colors(game.DisplayProfiles.Single()));
        Assert.False(game.DisplayProfiles.Single().ColorsPending);
    }

    // Application values without the mark, whatever the Windows profile has, are the user's.
    [Fact]
    public void ApplicationValuesThatAreNotPendingAreNeverReplaced()
    {
        var game = new ProfileListItem("Game");
        game.DisplayProfiles.Add(new ProfileDisplaySettings { DisplayName = "DELL", MonitorId = Dell, DigitalVibrance = 90 });
        var profiles = Profiles(game);

        Sync(profiles, Display(Dell, "DELL"));

        Assert.Equal(90, game.DisplayProfiles.Single().DigitalVibrance);
    }

    // ---- Copy, new profiles and the configuration -------------------------------------------

    [Fact]
    public void PendingColorsAreNeverCopiedAndRealOnesClearTheMark()
    {
        var source = new ProfileListItem("Source");
        source.DisplayProfiles.Add(new ProfileDisplaySettings { MonitorId = Dell, DigitalVibrance = 50, ColorsPending = true });
        source.DisplayProfiles.Add(new ProfileDisplaySettings { MonitorId = Lg, DigitalVibrance = 85 });
        var destination = new ProfileListItem("Destination");
        destination.DisplayProfiles.Add(new ProfileDisplaySettings { MonitorId = Dell, DigitalVibrance = 77 });
        destination.DisplayProfiles.Add(new ProfileDisplaySettings { MonitorId = Lg, DigitalVibrance = 50, ColorsPending = true });

        Assert.Equal(1, ProfileEditingCore.CopyDisplaySettings(source, destination));

        Assert.Equal(77, destination.DisplayProfiles[0].DigitalVibrance);
        Assert.Equal(85, destination.DisplayProfiles[1].DigitalVibrance);
        Assert.False(destination.DisplayProfiles[1].ColorsPending);
    }

    [Fact]
    public void ANewProfileMarksTheColorsOfAMonitorWithoutWindowsValues()
    {
        var windows = new ProfileListItem("Windows", isWindowsProfile: true);
        windows.DisplayProfiles.Add(new ProfileDisplaySettings { MonitorId = Dell, DigitalVibrance = 65 });

        var profile = ProfileDefaultsCore.NewProfile(windows, [Display(Dell, "DELL"), WithoutColors(Panel, "Built-in", 2)]);

        Assert.Equal((65, false), (profile.DisplayProfiles[0].DigitalVibrance, profile.DisplayProfiles[0].ColorsPending));
        Assert.Equal((50, true), (profile.DisplayProfiles[1].DigitalVibrance, profile.DisplayProfiles[1].ColorsPending));
    }

    [Fact]
    public void TheMarkIsSavedOnlyWhenSetAndValidated()
    {
        var config = new AppConfig
        {
            ApplicationProfiles =
            [
                new ApplicationProfileConfig
                {
                    Id = Guid.NewGuid(), Name = "Game", ExePaths = ["game.exe"], Enabled = true,
                    DisplayProfiles =
                    [
                        new ProfileDisplaySettings { DisplayName = "A", MonitorId = Dell },
                        new ProfileDisplaySettings { DisplayName = "B", MonitorId = Lg, ColorsPending = true },
                    ]
                }
            ]
        };
        var json = JsonSerializer.Serialize(config, NvProfileSwitcherJsonContext.Default.AppConfig);

        Assert.Single(JsonNode.Parse(json)!.ToJsonString().Split("ColorsPending").Skip(1));
        Assert.True(ConfigurationCore.TryValidateImportedConfiguration(json, out var imported));
        Assert.Equal([false, true], imported.ApplicationProfiles[0].DisplayProfiles.Select(v => v.ColorsPending));
        Assert.False(ConfigurationCore.TryValidateImportedConfiguration(json.Replace("\"ColorsPending\": true", "\"ColorsPending\": 1"), out _));
    }

    // ---- Writing and editing over the services ------------------------------------------------

    private static SwitchingFixture WithLgOnAnotherAdapter(bool lgHasWindowsValues = true)
    {
        var app = new SwitchingFixture();
        app.Backend.NotControlled.Add(Lg);
        if (!lgHasWindowsValues)
            foreach (var profile in new[] { app.Windows, app.Game, app.Tarkov, app.Notepad })
                profile.DisplayProfiles.RemoveAll(values => values.MonitorId == Lg);
        app.Displays.Refresh();
        return app;
    }

    [Fact]
    public void OnlyTheDisplaysTheApiControlsAreWrittenOrCountAsWritten()
    {
        using var app = WithLgOnAnotherAdapter();
        app.Foreground = "game.exe";

        app.Active.Check();

        Assert.Equal([Dell], app.Backend.Applied.Select(a => a.MonitorId));
        Assert.True(app.Colors.HasWritten(Dell));
        Assert.False(app.Colors.HasWritten(Lg));
        Assert.Equal("Game", app.Session.Switching.ActiveProfileName);
    }

    [Fact]
    public void WithoutTheApiNothingIsWrittenAndSwitchingWorks()
    {
        using var app = new SwitchingFixture(initialize: false);
        app.Displays.Refresh();
        app.Foreground = "game.exe";

        app.Active.Check();

        Assert.Equal(2, app.Displays.Displays.Count);
        Assert.All(app.Displays.Displays, d => Assert.False(d.CanAdjustColors));
        Assert.Empty(app.Factory.Created);
        Assert.Equal("Game", app.Session.Switching.ActiveProfileName);
    }

    [Fact]
    public void ColorEditsDoNothingOnADisplayWithoutColors()
    {
        using var app = WithLgOnAnotherAdapter();
        app.Editor.RefreshDisplays(app.Displays.Displays);
        app.Editor.SetSelection(app.Game);
        app.Editor.Begin(app.Game, Lg);
        var before = Colors(DisplayCore.FindValues(app.Game, Lg)!);

        app.Editing.SetValues(app.Editor.Values with { Vibrance = 99 });
        app.Editing.Reset();
        app.Editor.CompleteDraft();

        Assert.False(app.Editor.IsDirty);
        Assert.False(app.Preview.IsActive);
        Assert.Equal(before, Colors(DisplayCore.FindValues(app.Editor.Session!.Draft, Lg)!));
    }

    // Saving the Windows profile with a display without colors selected adds no Windows values.
    [Fact]
    public async Task SavingNeverMakesUpWindowsValues()
    {
        using var app = WithLgOnAnotherAdapter(lgHasWindowsValues: false);
        app.WindowActive = true;
        app.Editor.RefreshDisplays(app.Displays.Displays);
        app.Editor.SetSelection(app.Windows);
        app.Editor.Begin(app.Windows, Lg);

        await app.Editing.SaveAsync();

        Assert.Null(DisplayCore.FindValues(app.Windows, Lg));
    }

    [Fact]
    public void AResolutionForAMonitorWithoutWindowsValuesHoldsPendingColors()
    {
        using var app = WithLgOnAnotherAdapter(lgHasWindowsValues: false);
        app.Editor.RefreshDisplays(app.Displays.Displays);
        app.Editor.SetSelection(app.Game);
        app.Editor.Begin(app.Game, Lg);

        app.Editor.SetResolution(new DisplayResolution(1280, 720));

        var values = DisplayCore.FindValues(app.Editor.Session!.Draft, Lg)!;
        Assert.True(values.ColorsPending);
        Assert.Equal(new DisplayResolution(1280, 720), values.Resolution);
    }

    [Fact]
    public void AResolutionForAMonitorWithWindowsValuesUsesThem()
    {
        using var app = WithLgOnAnotherAdapter();
        app.Game.DisplayProfiles.RemoveAll(values => values.MonitorId == Lg);
        app.Editor.RefreshDisplays(app.Displays.Displays);
        app.Editor.SetSelection(app.Game);
        app.Editor.Begin(app.Game, Lg);

        app.Editor.SetResolution(new DisplayResolution(1280, 720));

        var values = DisplayCore.FindValues(app.Editor.Session!.Draft, Lg)!;
        Assert.False(values.ColorsPending);
        Assert.Equal(55, values.DigitalVibrance);
    }
}
