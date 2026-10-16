using NvProfileSwitcher.Nvidia;
using Xunit;
using static NvProfileSwitcher.Core.Tests.FakeDisplayBackend;

namespace NvProfileSwitcher.Core.Tests;

public sealed class DisplayProfileSyncCoreTests
{
    private const string Dell = "DEL4321-ABC123";
    private const string Lg = "GSM5B7F-305NTAB1C2D3";

    private static List<ProfileListItem> Profiles(params ProfileListItem[] applications) =>
        [new ProfileListItem("Windows", isWindowsProfile: true), .. applications];

    private static bool Synchronize(
        List<ProfileListItem> profiles,
        IReadOnlyList<NvidiaDisplay> displays,
        int? vibrance = 70,
        int? hue = 15) =>
        DisplayProfileSyncCore.Synchronize(profiles, displays, NoSharedIds, 0, _ => vibrance, _ => hue).Changed;

    private static readonly IReadOnlySet<string> NoSharedIds = new HashSet<string>();

    [Fact]
    public void NewDisplayStartsFromTheCurrentValuesAndIsClonedToApplicationProfiles()
    {
        var game = new ProfileListItem("Game") { Enabled = true };
        var profiles = Profiles(game);

        Assert.True(Synchronize(profiles, [Display(Dell, "DELL U2720Q", number: 2)]));

        var windows = Assert.Single(profiles[0].DisplayProfiles);
        Assert.Equal("Display 2", windows.Name);
        Assert.Equal("DELL U2720Q", windows.DisplayName);
        Assert.Equal(Dell, windows.MonitorId);
        Assert.Equal((100d, 100d, 1d, 70, 15), (windows.Brightness, windows.Contrast, windows.Gamma, windows.DigitalVibrance, windows.Hue));

        var clone = Assert.Single(game.DisplayProfiles);
        Assert.Null(clone.Name);
        Assert.Equal(("DELL U2720Q", Dell), (clone.DisplayName, clone.MonitorId));
        Assert.Equal((100d, 100d, 1d, 70, 15), (clone.Brightness, clone.Contrast, clone.Gamma, clone.DigitalVibrance, clone.Hue));
        Assert.NotSame(windows, clone);
    }

    [Fact]
    public void ApplicationProfilesCloneTheExistingWindowsValues()
    {
        var game = new ProfileListItem("Game");
        var profiles = Profiles(game);
        profiles[0].DisplayProfiles.Add(new ProfileDisplaySettings
        {
            Name = "Display 1", DisplayName = "DELL U2720Q", MonitorId = Dell,
            Brightness = 98, Contrast = 102, Gamma = 0.95, DigitalVibrance = 55, Hue = 3
        });

        Assert.True(Synchronize(profiles, [Display(Dell, "DELL U2720Q")]));

        var clone = Assert.Single(game.DisplayProfiles);
        Assert.Equal((98d, 102d, 0.95, 55, 3), (clone.Brightness, clone.Contrast, clone.Gamma, clone.DigitalVibrance, clone.Hue));
    }

    [Fact]
    public void UnreadableValuesUseTheNvidiaDefaults()
    {
        var profiles = Profiles();

        Synchronize(profiles, [Display(Dell, "DELL U2720Q")], vibrance: null, hue: null);

        var windows = Assert.Single(profiles[0].DisplayProfiles);
        Assert.Equal((50, 0), (windows.DigitalVibrance, windows.Hue));
    }

    [Fact]
    public void RenamedDisplayIsUpdatedInEveryProfile()
    {
        var game = new ProfileListItem("Game");
        var profiles = Profiles(game);
        Synchronize(profiles, [Display(Dell, "DELL U2720Q")]);

        Assert.True(Synchronize(profiles, [Display(Dell, "DELL U2720QM")]));

        Assert.Equal("DELL U2720QM", profiles[0].DisplayProfiles[0].DisplayName);
        Assert.Equal("DELL U2720QM", game.DisplayProfiles[0].DisplayName);
        Assert.Single(profiles[0].DisplayProfiles);
        Assert.Single(game.DisplayProfiles);
    }

    [Fact]
    public void UnchangedProfilesReportNoChange()
    {
        var profiles = Profiles(new ProfileListItem("Game"));
        IReadOnlyList<NvidiaDisplay> displays = [Display(Dell, "DELL U2720Q"), Display(Lg, "LG ULTRAGEAR", 2)];
        Synchronize(profiles, displays);

        Assert.False(Synchronize(profiles, displays));
    }

    [Fact]
    public void MonitorIdsAreMatchedIgnoringCase()
    {
        var profiles = Profiles();
        Synchronize(profiles, [Display(Dell, "DELL U2720Q")]);

        Assert.False(Synchronize(profiles, [Display(Dell.ToLowerInvariant(), "DELL U2720Q")]));
        Assert.Single(profiles[0].DisplayProfiles);
    }

    // Displays without an identity never reach the synchronization: the identification leaves them out.
    [Fact]
    public void DisplaysWithoutMonitorIdAreIgnored()
    {
        var profiles = Profiles(new ProfileListItem("Game"));
        var identified = MonitorIdentityCore.Identify([Display("", "Generic PnP Monitor"), Display("  ", "Other", 2)], NoSharedIds);

        Assert.Empty(identified.Displays);
        Assert.Equal(2, identified.UnidentifiedCount);
        Assert.False(Synchronize(profiles, identified.Displays));
        Assert.Empty(profiles[0].DisplayProfiles);
    }

    [Fact]
    public void WithoutWindowsProfileNothingChanges()
    {
        var game = new ProfileListItem("Game");

        Assert.False(DisplayProfileSyncCore.Synchronize([game], [Display(Dell, "DELL U2720Q")], NoSharedIds, 0, _ => 70, _ => 15).Changed);
        Assert.Empty(game.DisplayProfiles);
    }

    [Theory]
    [InlineData(@"\\.\DISPLAY1", "Display 1")]
    [InlineData(@"\\.\DISPLAY12", "Display 12")]
    [InlineData(@"\\.\display3", "Display 3")]
    [InlineData(@"\\.\DISPLAY", "Display")]
    [InlineData("MONITOR", "Display")]
    public void WindowsProfileEntriesAreNamedAfterTheDisplayNumber(string gdiName, string expected) =>
        Assert.Equal(expected, DisplayProfileSyncCore.WindowsProfileJsonName(gdiName));

    // Identities without serial number of one model, on the connections of displays 1 to 4.
    private const string Model = "GSM5B7F";
    private static string At(int number) => $"{Model}@{Instance(number)}";

    private static NvidiaDisplay WithoutSerial(int number) =>
        Display("", $"LG {number}", number) with { MonitorId = At(number), Monitor = new MonitorDescriptor(Model, Instance(number), null) };

    private static ProfileDisplaySettings Values(string monitorId, int vibrance) =>
        new() { DisplayName = monitorId, MonitorId = monitorId, DigitalVibrance = vibrance };

    private static DisplaySyncResult Sync(
        List<ProfileListItem> profiles, IReadOnlyList<NvidiaDisplay> displays, IReadOnlySet<string>? shared = null, int unidentified = 0) =>
        DisplayProfileSyncCore.Synchronize(
            profiles, displays, shared ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase), unidentified, _ => 70, _ => 15);

    private static int Vibrance(ProfileListItem profile, string monitorId) => DisplayCore.FindValues(profile, monitorId)!.DigitalVibrance;

    [Fact]
    public void IdenticalMonitorsWithoutSerialKeepTheirOwnValues()
    {
        var game = new ProfileListItem("Game");
        game.DisplayProfiles.AddRange([Values(At(1), 20), Values(At(2), 80)]);
        var profiles = Profiles(game);
        profiles[0].DisplayProfiles.AddRange([Values(At(1), 50), Values(At(2), 50)]);

        Sync(profiles, [WithoutSerial(1), WithoutSerial(2)]);

        Assert.Equal((20, 80), (Vibrance(game, At(1)), Vibrance(game, At(2))));
    }

    // A monitor that moved to another connection keeps its values when the enumeration leaves
    // no doubt about it: one new connection and one saved connection of its model.
    [Fact]
    public void AMonitorOnANewConnectionAdoptsTheOnlySavedConnectionOfItsModel()
    {
        var game = new ProfileListItem("Game");
        game.DisplayProfiles.Add(Values(At(1), 20));
        var profiles = Profiles(game);
        profiles[0].DisplayProfiles.Add(Values(At(1), 55));

        var result = Sync(profiles, [WithoutSerial(3)]);

        var remap = Assert.Single(result.Remaps);
        Assert.Equal(At(1), remap.From);
        Assert.Equal([At(3)], remap.To);
        Assert.True(result.Changed);
        Assert.Equal((55, 20), (Vibrance(profiles[0], At(3)), Vibrance(game, At(3))));
        Assert.Null(DisplayCore.FindValues(game, At(1)));
        Assert.Equal("LG 3", DisplayCore.FindValues(game, At(3))!.DisplayName);
    }

    // A laptop on a dock and on a direct cable: the values follow the monitor every time.
    [Fact]
    public void AMonitorMovingBackAndForthKeepsItsValues()
    {
        var profiles = Profiles();
        profiles[0].DisplayProfiles.Add(Values(At(1), 55));

        Sync(profiles, [WithoutSerial(2)]);
        Sync(profiles, [WithoutSerial(1)]);

        Assert.Equal(55, Vibrance(profiles[0], At(1)));
        Assert.Single(profiles[0].DisplayProfiles);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TwoNewConnectionsAndOneSavedConnectionAdoptNothing(bool reversed)
    {
        var profiles = Profiles();
        profiles[0].DisplayProfiles.Add(Values(At(1), 55));
        NvidiaDisplay[] displays = reversed ? [WithoutSerial(3), WithoutSerial(2)] : [WithoutSerial(2), WithoutSerial(3)];

        var result = Sync(profiles, displays);

        Assert.Empty(result.Remaps);
        Assert.Equal(55, Vibrance(profiles[0], At(1)));
        Assert.Equal((70, 70), (Vibrance(profiles[0], At(2)), Vibrance(profiles[0], At(3))));
    }

    [Fact]
    public void OneNewConnectionAndTwoSavedConnectionsAdoptNothing()
    {
        var profiles = Profiles();
        profiles[0].DisplayProfiles.AddRange([Values(At(1), 55), Values(At(2), 45)]);

        Assert.Empty(Sync(profiles, [WithoutSerial(3)]).Remaps);
        Assert.Equal(70, Vibrance(profiles[0], At(3)));
    }

    // Identical monitors reconnected one at a time: while both saved connections are free, no
    // new connection can tell which one it is, in any refresh.
    [Fact]
    public void IdenticalMonitorsAppearingOneAtATimeAdoptNothing()
    {
        var profiles = Profiles();
        profiles[0].DisplayProfiles.AddRange([Values(At(1), 55), Values(At(2), 45)]);

        Assert.Empty(Sync(profiles, [WithoutSerial(3)]).Remaps);
        Assert.Empty(Sync(profiles, [WithoutSerial(3), WithoutSerial(4)]).Remaps);

        Assert.Equal((55, 45), (Vibrance(profiles[0], At(1)), Vibrance(profiles[0], At(2))));
    }

    [Fact]
    public void AConnectedOrOtherModelConnectionIsNotAdopted()
    {
        var profiles = Profiles();
        var otherModel = $"DEL4321@{Instance(4)}";
        profiles[0].DisplayProfiles.AddRange([Values(At(1), 55), Values(otherModel, 45)]);

        Assert.Empty(Sync(profiles, [WithoutSerial(1), WithoutSerial(2)]).Remaps);
        Assert.Equal(55, Vibrance(profiles[0], At(1)));
        Assert.Equal(45, Vibrance(profiles[0], otherModel));
    }

    // A display without identity could be one of the monitors, so nothing is adopted until all have one.
    [Fact]
    public void NothingIsAdoptedWhileADisplayHasNoIdentity()
    {
        var profiles = Profiles();
        profiles[0].DisplayProfiles.Add(Values(At(1), 55));

        Assert.Empty(Sync(profiles, [WithoutSerial(3)], unidentified: 1).Remaps);
        Assert.Equal(55, Vibrance(profiles[0], At(1)));
    }

    [Fact]
    public void ThePhysicalValuesOfASharedSerialGoToEachConnectionAndAreRemoved()
    {
        const string physical = "SAM0F12-1";
        var game = new ProfileListItem("Game");
        game.DisplayProfiles.Add(Values(physical, 33));
        var profiles = Profiles(game);
        profiles[0].DisplayProfiles.Add(Values(physical, 52));
        var shared = new HashSet<string>([physical], StringComparer.OrdinalIgnoreCase);
        NvidiaDisplay Twin(int number) => Display("", $"Samsung {number}", number) with
        {
            MonitorId = $"SAM0F12@{Instance(number)}",
            PhysicalMonitorId = physical,
            Monitor = new MonitorDescriptor("SAM0F12", Instance(number), "1")
        };

        var result = Sync(profiles, [Twin(1), Twin(2)], shared);

        Assert.Equal(physical, Assert.Single(result.Remaps).From);
        foreach (var number in new[] { 1, 2 })
        {
            Assert.Equal(52, Vibrance(profiles[0], $"SAM0F12@{Instance(number)}"));
            Assert.Equal(33, Vibrance(game, $"SAM0F12@{Instance(number)}"));
        }

        Assert.All(profiles, profile => Assert.Null(DisplayCore.FindValues(profile, physical)));
    }

    // The NVIDIA Control Panel settings start the brightness, contrast and gamma of a display
    // new to the Windows profile; vibrance and hue still come from the API.
    private static DisplaySyncResult SyncWithControlPanel(
        List<ProfileListItem> profiles, IReadOnlyList<NvidiaDisplay> displays, Func<NvidiaDisplay, ControlPanelColors?> controlPanel) =>
        DisplayProfileSyncCore.Synchronize(profiles, displays, NoSharedIds, 0, _ => 60, _ => 0, controlPanel);

    private static (double, double, double, int, int) Colors(ProfileDisplaySettings values) =>
        (values.Brightness, values.Contrast, values.Gamma, values.DigitalVibrance, values.Hue);

    [Fact]
    public void ANewDisplayStartsFromTheControlPanelSettings()
    {
        var profiles = Profiles();

        Assert.True(SyncWithControlPanel(profiles, [Display(Dell, "DELL U2720Q")], _ => new ControlPanelColors(105, 110, 1.3)).Changed);

        Assert.Equal((105d, 110d, 1.3, 60, 0), Colors(Assert.Single(profiles[0].DisplayProfiles)));
    }

    [Fact]
    public void EachDisplayStartsFromItsOwnControlPanelSettings()
    {
        const string asus1 = "AUS27A1-S1", asus2 = "AUS27A1-S2", portable = "AUS16B2-S3";
        var settings = new Dictionary<string, ControlPanelColors>
        {
            [asus1] = new(105, 110, 1.3),
            [asus2] = new(110, 105, 1.2),
            [portable] = new(95, 95, 1.1)
        };
        var profiles = Profiles();

        SyncWithControlPanel(profiles, [Display(asus1, "ASUS VG27AQL3A", 1), Display(asus2, "ASUS VG27AQL3A", 2), Display(portable, "ASUS MB16NCG", 3)],
            display => settings[display.MonitorId]);

        Assert.Equal((105d, 110d, 1.3, 60, 0), Colors(DisplayCore.FindValues(profiles[0], asus1)!));
        Assert.Equal((110d, 105d, 1.2, 60, 0), Colors(DisplayCore.FindValues(profiles[0], asus2)!));
        Assert.Equal((95d, 95d, 1.1, 60, 0), Colors(DisplayCore.FindValues(profiles[0], portable)!));
    }

    [Fact]
    public void WithoutUsableControlPanelSettingsANewDisplayStartsNeutral()
    {
        var profiles = Profiles();

        SyncWithControlPanel(profiles, [Display(Dell, "DELL U2720Q")], _ => null);

        Assert.Equal((100d, 100d, 1d, 60, 0), Colors(Assert.Single(profiles[0].DisplayProfiles)));
    }

    // A display the Windows profile has values for keeps them, in every profile, and its control
    // panel settings are not even read: they start a display once.
    [Fact]
    public void KnownDisplaysKeepTheirValuesAndAreNotRead()
    {
        var game = new ProfileListItem("Game");
        game.DisplayProfiles.Add(new ProfileDisplaySettings { DisplayName = "DELL U2720Q", MonitorId = Dell, Brightness = 90, Contrast = 95, Gamma = 0.9, DigitalVibrance = 80, Hue = 5 });
        var profiles = Profiles(game);
        profiles[0].DisplayProfiles.Add(new ProfileDisplaySettings { Name = "Display 1", DisplayName = "DELL U2720Q", MonitorId = Dell, Brightness = 98, Contrast = 102, Gamma = 0.95, DigitalVibrance = 55, Hue = 3 });
        var read = new List<string>();

        Assert.False(SyncWithControlPanel(profiles, [Display(Dell, "DELL U2720Q")], display =>
        {
            read.Add(display.MonitorId);
            return new ControlPanelColors(120, 120, 2.8);
        }).Changed);

        Assert.Empty(read);
        Assert.Equal((98d, 102d, 0.95, 55, 3), Colors(Assert.Single(profiles[0].DisplayProfiles)));
        Assert.Equal((90d, 95d, 0.9, 80, 5), Colors(Assert.Single(game.DisplayProfiles)));
    }

    // Only the display new to the Windows profile is read; an application profile that has
    // values for it keeps them, and one without takes the Windows values, as before.
    [Fact]
    public void ApplicationProfilesKeepTheirValuesAndOthersCloneTheWindowsValues()
    {
        var game = new ProfileListItem("Game");
        game.DisplayProfiles.Add(new ProfileDisplaySettings { DisplayName = "LG", MonitorId = Lg, Brightness = 90, Contrast = 95, Gamma = 0.9, DigitalVibrance = 80, Hue = 5 });
        var work = new ProfileListItem("Work");
        var profiles = Profiles(game, work);
        var read = new List<string>();

        SyncWithControlPanel(profiles, [Display(Lg, "LG ULTRAGEAR")], display =>
        {
            read.Add(display.MonitorId);
            return new ControlPanelColors(110, 105, 1.2);
        });

        Assert.Equal([Lg], read);
        Assert.Equal((110d, 105d, 1.2, 60, 0), Colors(Assert.Single(profiles[0].DisplayProfiles)));
        Assert.Equal((90d, 95d, 0.9, 80, 5), Colors(Assert.Single(game.DisplayProfiles)));
        Assert.Equal((110d, 105d, 1.2, 60, 0), Colors(Assert.Single(work.DisplayProfiles)));
    }

    // A monitor that adopts the values of its previous connection keeps them: it is not new.
    [Fact]
    public void AnAdoptedMonitorKeepsItsValuesAndIsNotRead()
    {
        var profiles = Profiles();
        profiles[0].DisplayProfiles.Add(new ProfileDisplaySettings { Name = "Display 1", DisplayName = "LG 1", MonitorId = At(1), Brightness = 90, DigitalVibrance = 40 });
        var read = new List<string>();

        SyncWithControlPanel(profiles, [WithoutSerial(2)], display =>
        {
            read.Add(display.MonitorId);
            return new ControlPanelColors(120, 120, 2.8);
        });

        Assert.Empty(read);
        Assert.Equal((90d, 100d, 1d, 40, 0), Colors(Assert.Single(profiles[0].DisplayProfiles)));
    }
}
