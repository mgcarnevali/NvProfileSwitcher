using NvProfileSwitcher.Core;
using NvProfileSwitcher.Models;
using NvProfileSwitcher.Nvidia;
using NvProfileSwitcher.Services;
using Xunit;
using static NvProfileSwitcher.Core.Tests.SwitchingFixture;

namespace NvProfileSwitcher.Core.Tests;

// Display modes over the switching services: the Dell is \\.\DISPLAY1 and the LG \\.\DISPLAY2.
public sealed class DisplayModeControllerTests
{
    private const string DellGdi = @"\\.\DISPLAY1";
    private const string LgGdi = @"\\.\DISPLAY2";

    private static readonly DisplayModeInfo DellDesktop = new(2560, 1440, 144);
    private static readonly DisplayModeInfo Dell1080At144 = new(1920, 1080, 144);
    private static readonly DisplayModeInfo Dell1080At60 = new(1920, 1080, 60);
    private static readonly DisplayModeInfo Dell960At120 = new(1280, 960, 120);
    private static readonly DisplayModeInfo LgDesktop = new(1920, 1080, 60);
    private static readonly DisplayModeInfo Lg720At60 = new(1280, 720, 60);

    private static SwitchingFixture Fixture(bool withModes = true)
    {
        var app = new SwitchingFixture();
        if (withModes)
        {
            app.DisplayModes.AddMonitor(DellGdi, DellDesktop, Dell1080At144, Dell1080At60, Dell960At120);
            app.DisplayModes.AddMonitor(LgGdi, LgDesktop, Lg720At60);
        }

        return app;
    }

    private static void SetResolution(ProfileListItem profile, string monitorId, int width, int height) =>
        profile.DisplayProfiles.Single(values => values.MonitorId == monitorId).Resolution = new DisplayResolution(width, height);

    private static void Focus(SwitchingFixture app, string executable)
    {
        app.Foreground = executable;
        app.Active.Check();
    }

    private static void Disconnect(SwitchingFixture app, string monitorId)
    {
        app.Backend.Displays.RemoveAll(display => display.MonitorId == monitorId);
        app.Nvidia.RefreshDisplays();
    }

    private static void Connect(SwitchingFixture app, NvidiaDisplay display)
    {
        app.Backend.Displays.Add(display);
        app.Nvidia.RefreshDisplays();
    }

    private static string RecoveryPath(SwitchingFixture app) =>
        Path.Combine(Path.GetDirectoryName(app.Session.ConfigPath)!, "display-modes.json");

    private static DisplayModeRecoveryStore Recovery(SwitchingFixture app) => new(RecoveryPath(app));

    [Fact]
    public void FocusAppliesTheResolutionAtTheDesktopRefreshRateBeforeTheColors()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, 1920, 1080);
        var colorsWhenApplied = -1;
        app.DisplayModes.DuringApply = () => colorsWhenApplied = app.Backend.Applied.Count;

        Focus(app, "game.exe");

        Assert.Equal([(DellGdi, Dell1080At144)], app.DisplayModes.Applied);
        Assert.Equal(0, colorsWhenApplied);
        Assert.Equal(["Game", "Game"], app.AppliedProfiles());
        // The LG has no resolution: it is never read or changed.
        Assert.DoesNotContain(app.DisplayModes.Calls, call => call.Contains(LgGdi));
    }

    [Fact]
    public void AltTabRestoresTheDesktopModeAndReturningAppliesItAgain()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, 1920, 1080);

        Focus(app, "game.exe");
        Focus(app, "explorer.exe");
        Assert.Equal(DellDesktop, app.DisplayModes.Current[DellGdi]);
        Assert.False(app.Services.Modes.OwnsAnyMonitor);
        Assert.False(File.Exists(RecoveryPath(app)));

        Focus(app, "game.exe");
        Assert.Equal(Dell1080At144, app.DisplayModes.Current[DellGdi]);
        Assert.Equal([Dell1080At144, DellDesktop, Dell1080At144], app.DisplayModes.Applied.Select(a => a.Mode));
    }

    [Fact]
    public void ARefreshRateTheResolutionLacksFallsBackToItsHighest()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, 1280, 960);

        Focus(app, "game.exe");

        Assert.Equal(Dell960At120, app.DisplayModes.Current[DellGdi]);
    }

    [Fact]
    public void SwitchingBetweenProfilesKeepsTheDesktopModeAsTheReference()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, 1280, 960);
        SetResolution(app.Notepad, Dell, 1920, 1080);

        Focus(app, "game.exe");
        Focus(app, "notepad.exe");
        // At 144 Hz, the desktop rate, not the 120 Hz the previous profile applied.
        Assert.Equal(Dell1080At144, app.DisplayModes.Current[DellGdi]);

        Focus(app, "explorer.exe");
        Assert.Equal(DellDesktop, app.DisplayModes.Current[DellGdi]);
        Assert.False(app.Services.Modes.OwnsAnyMonitor);
    }

    [Fact]
    public void ProfilesWithTheSameResolutionDoNotChangeTheModeAgain()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, 1920, 1080);
        SetResolution(app.Notepad, Dell, 1920, 1080);

        Focus(app, "game.exe");
        Focus(app, "notepad.exe");

        Assert.Single(app.DisplayModes.Applied);
    }

    [Fact]
    public void RapidSwitchesEndOnTheLastTarget()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, 1920, 1080);

        for (var i = 0; i < 5; i++)
        {
            Focus(app, "game.exe");
            Focus(app, "explorer.exe");
        }

        Assert.Equal(DellDesktop, app.DisplayModes.Current[DellGdi]);
        Assert.Equal(10, app.DisplayModes.Applied.Count);
        Assert.False(app.Services.Modes.OwnsAnyMonitor);
    }

    [Fact]
    public void AProfileWithoutResolutionRestoresOnlyWhatWasChanged()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, 1920, 1080);

        Focus(app, "game.exe");
        Focus(app, "notepad.exe");

        Assert.Equal(DellDesktop, app.DisplayModes.Current[DellGdi]);
        Assert.Equal(["Game", "Game", "notepad.exe", "notepad.exe"], app.AppliedProfiles());
    }

    [Fact]
    public void ModesChangedIsRaisedOnlyForEffectiveChanges()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, 2560, 1440);
        SetResolution(app.Notepad, Dell, 1920, 1080);
        var raised = 0;
        app.Services.Modes.ModesChanged += () => raised++;

        // The desktop resolution: nothing to change.
        Focus(app, "game.exe");
        Assert.Equal(0, raised);
        Assert.Empty(app.DisplayModes.Applied);
        Assert.False(app.Services.Modes.OwnsAnyMonitor);

        Focus(app, "notepad.exe");
        Assert.Equal(1, raised);
        Focus(app, "explorer.exe");
        Assert.Equal(2, raised);
    }

    [Fact]
    public void ADisplayChangeAfterOurOwnChangeChangesNothing()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, 1920, 1080);
        Focus(app, "game.exe");
        var applied = app.DisplayModes.Applied.Count;

        app.Services.Modes.OnDisplaysChanged();
        app.Services.Modes.OnDisplaysChanged();

        Assert.Equal(applied, app.DisplayModes.Applied.Count);
    }

    [Fact]
    public void AModeAGameOrTheUserSetIsNeverOverwritten()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, 1920, 1080);
        Focus(app, "game.exe");

        // The game switches to 1280×960 itself; the display change does not fight it.
        app.DisplayModes.Current[DellGdi] = Dell960At120;
        app.Services.Modes.OnDisplaysChanged();
        Assert.Equal(Dell960At120, app.DisplayModes.Current[DellGdi]);

        // Leaving the game does not restore a mode this application did not set.
        Focus(app, "explorer.exe");
        Assert.Equal(Dell960At120, app.DisplayModes.Current[DellGdi]);
        Assert.False(app.Services.Modes.OwnsAnyMonitor);
        Assert.False(File.Exists(RecoveryPath(app)));
    }

    [Fact]
    public void AChangeWindowsRejectsIsNotRetriedAndTheOtherMonitorStillChanges()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, 1920, 1080);
        SetResolution(app.Game, Lg, 1280, 720);
        app.DisplayModes.Rejected.Add(DellGdi);

        Focus(app, "game.exe");
        app.Services.Modes.OnDisplaysChanged();

        Assert.Equal(DellDesktop, app.DisplayModes.Current[DellGdi]);
        Assert.Equal(Lg720At60, app.DisplayModes.Current[LgGdi]);
        Assert.Single(app.DisplayModes.Calls, call => call.StartsWith($"apply {DellGdi}", StringComparison.Ordinal));
        Assert.Empty(app.Dialogs.Messages);
        Assert.Equal(["Game", "Game"], app.AppliedProfiles());

        Focus(app, "explorer.exe");
        Assert.Equal(LgDesktop, app.DisplayModes.Current[LgGdi]);
        Assert.False(app.Services.Modes.OwnsAnyMonitor);
    }

    [Fact]
    public void AResolutionTheMonitorDoesNotSupportChangesNothing()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, 800, 600);

        Focus(app, "game.exe");

        Assert.Empty(app.DisplayModes.Applied);
        Assert.False(app.Services.Modes.OwnsAnyMonitor);
    }

    [Fact]
    public void AChangeThatCannotBeRecordedIsNotMade()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, 1920, 1080);
        // A directory where the recovery file goes: it cannot be written.
        Directory.CreateDirectory(RecoveryPath(app) + ".tmp");

        Focus(app, "game.exe");

        Assert.Empty(app.DisplayModes.Applied);
        Assert.False(app.Services.Modes.OwnsAnyMonitor);
        // The colors are applied all the same.
        Assert.Equal(["Game", "Game"], app.AppliedProfiles());
    }

    [Fact]
    public void TheChangeIsRecordedBeforeItIsMade()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, 1920, 1080);
        IReadOnlyList<DisplayModeRecoveryEntry>? duringChange = null;
        app.DisplayModes.DuringApply = () => duringChange = Recovery(app).Load();

        Focus(app, "game.exe");

        var entry = Assert.Single(duringChange!);
        Assert.Equal(Dell, entry.MonitorId);
        Assert.Equal(DellDesktop, entry.Original);
        Assert.Equal(Dell1080At144, entry.Target);
        var saved = Assert.Single(Recovery(app).Load()!);
        Assert.Equal(Dell1080At144, saved.Applied);
    }

    [Fact]
    public void AFailedRestoreKeepsTheEntryAndTheNextDisplayChangeRetriesIt()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, 1920, 1080);
        Focus(app, "game.exe");

        app.DisplayModes.Rejected.Add(DellGdi);
        Focus(app, "explorer.exe");
        Assert.Equal(Dell1080At144, app.DisplayModes.Current[DellGdi]);
        Assert.True(app.Services.Modes.OwnsAnyMonitor);
        Assert.Single(Recovery(app).Load()!);

        app.DisplayModes.Rejected.Clear();
        app.Services.Modes.OnDisplaysChanged();
        Assert.Equal(DellDesktop, app.DisplayModes.Current[DellGdi]);
        Assert.False(app.Services.Modes.OwnsAnyMonitor);
        Assert.False(File.Exists(RecoveryPath(app)));
    }

    [Fact]
    public void ADisconnectedMonitorIsRestoredWhenItComesBack()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, 1920, 1080);
        Focus(app, "game.exe");

        // The Dell is disconnected while the game runs, and the game is closed.
        Disconnect(app, Dell);
        app.Services.Modes.OnDisplaysChanged();
        Focus(app, "explorer.exe");
        Assert.True(app.Services.Modes.OwnsAnyMonitor);
        Assert.Single(Recovery(app).Load()!);

        Connect(app, FakeDisplayBackend.Display(Dell, "DELL U2720Q"));
        app.Services.Modes.OnDisplaysChanged();

        Assert.Equal(DellDesktop, app.DisplayModes.Current[DellGdi]);
        Assert.False(app.Services.Modes.OwnsAnyMonitor);
    }

    // Windows gives a reconnected monitor its desktop mode: a monitor missing from an enumeration
    // gets the profile's resolution again when it returns (a DisplayPort monitor turned off).
    [Fact]
    public void AMonitorThatReturnsWhileTheProfileIsActiveGetsItsResolutionAgain()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, 1920, 1080);
        Focus(app, "game.exe");

        Disconnect(app, Dell);
        app.Services.Modes.OnDisplaysChanged();
        app.DisplayModes.Current[DellGdi] = DellDesktop;
        Connect(app, FakeDisplayBackend.Display(Dell, "DELL U2720Q"));
        app.Services.Modes.OnDisplaysChanged();

        Assert.Equal(Dell1080At144, app.DisplayModes.Current[DellGdi]);
        // Its desktop mode is still the one read before the first change.
        Focus(app, "explorer.exe");
        Assert.Equal(DellDesktop, app.DisplayModes.Current[DellGdi]);
        Assert.False(app.Services.Modes.OwnsAnyMonitor);
    }

    [Fact]
    public void AMonitorThatReturnsStillShowingTheResolutionIsNotChangedAgain()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, 1920, 1080);
        Focus(app, "game.exe");

        Disconnect(app, Dell);
        app.Services.Modes.OnDisplaysChanged();
        Connect(app, FakeDisplayBackend.Display(Dell, "DELL U2720Q"));
        app.Services.Modes.OnDisplaysChanged();

        Assert.Single(app.DisplayModes.Applied);
        Assert.Equal(Dell1080At144, app.DisplayModes.Current[DellGdi]);
    }

    // Only a monitor missing from an enumeration counts as new: a connected one keeps a mode a
    // game set, display change after display change.
    [Fact]
    public void AConnectedMonitorIsNeverChangedAgainForTheSameTarget()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, 1920, 1080);
        Focus(app, "game.exe");

        app.DisplayModes.Current[DellGdi] = DellDesktop;
        app.Services.Modes.OnDisplaysChanged();
        app.Services.Modes.OnDisplaysChanged();

        Assert.Single(app.DisplayModes.Applied);
        Assert.Equal(DellDesktop, app.DisplayModes.Current[DellGdi]);
    }

    [Fact]
    public void AMonitorConnectedWhileTheProfileIsActiveGetsItsResolution()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, 1920, 1080);
        Disconnect(app, Dell);
        Focus(app, "game.exe");
        Assert.Empty(app.DisplayModes.Applied);

        Connect(app, FakeDisplayBackend.Display(Dell, "DELL U2720Q"));
        app.Services.Modes.OnDisplaysChanged();

        Assert.Equal(Dell1080At144, app.DisplayModes.Current[DellGdi]);
    }

    [Fact]
    public void GlobalSwitchingOffRestoresAndOnAppliesAgain()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, 1920, 1080);
        Focus(app, "game.exe");

        app.Automatic.SetGlobalEnabled(false);
        Assert.Equal(DellDesktop, app.DisplayModes.Current[DellGdi]);

        // While paused, a focus change never applies it.
        app.Active.Check();
        Assert.Equal(DellDesktop, app.DisplayModes.Current[DellGdi]);

        app.Automatic.SetGlobalEnabled(true);
        Assert.Equal(Dell1080At144, app.DisplayModes.Current[DellGdi]);
    }

    [Fact]
    public void TheWindowsOverrideRestoresAndAProfileOverrideApplies()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, 1920, 1080);
        Focus(app, "game.exe");

        app.Automatic.ToggleWindowsOverride();
        Assert.Equal(DellDesktop, app.DisplayModes.Current[DellGdi]);

        app.Automatic.ResumeAutomatic();
        Assert.Equal(Dell1080At144, app.DisplayModes.Current[DellGdi]);

        Focus(app, "explorer.exe");
        app.Automatic.ToggleProfileOverride(app.Session.Profiles.ApplicationProfiles.ToList().IndexOf(app.Game));
        Assert.Equal(Dell1080At144, app.DisplayModes.Current[DellGdi]);
    }

    [Fact]
    public void RestoreOriginalsRestoresEveryMonitorOnExit()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, 1920, 1080);
        SetResolution(app.Game, Lg, 1280, 720);
        Focus(app, "game.exe");

        app.Services.Modes.RestoreOriginals();

        Assert.Equal(DellDesktop, app.DisplayModes.Current[DellGdi]);
        Assert.Equal(LgDesktop, app.DisplayModes.Current[LgGdi]);
        Assert.False(File.Exists(RecoveryPath(app)));
    }

    [Fact]
    public void TheFirstDisplayEnumerationRestoresWhatAPreviousSessionLeftChanged()
    {
        using var app = Fixture();
        app.DisplayModes.Current[DellGdi] = Dell1080At144;
        app.DisplayModes.Current[LgGdi] = Lg720At60;
        Recovery(app).Save([
            new DisplayModeRecoveryEntry(Dell, DellDesktop, Dell1080At144, Dell1080At144),
            // Changed since by someone else: left alone.
            new DisplayModeRecoveryEntry(Lg, LgDesktop, new DisplayModeInfo(1024, 768, 60), new DisplayModeInfo(1024, 768, 60)),
            // Not connected: kept for a later start.
            new DisplayModeRecoveryEntry("ACR0001-XYZ", new DisplayModeInfo(3840, 2160, 60), new DisplayModeInfo(1920, 1080, 60), null),
        ]);

        app.Services.Modes.OnDisplaysChanged();

        Assert.Equal(DellDesktop, app.DisplayModes.Current[DellGdi]);
        Assert.Equal(Lg720At60, app.DisplayModes.Current[LgGdi]);
        var kept = Assert.Single(Recovery(app).Load()!);
        Assert.Equal("ACR0001-XYZ", kept.MonitorId);

        // Only the first enumeration reads the file.
        File.Delete(RecoveryPath(app));
        Recovery(app).Save([new DisplayModeRecoveryEntry(Lg, LgDesktop, Lg720At60, Lg720At60)]);
        app.Services.Modes.OnDisplaysChanged();
        Assert.Equal(Lg720At60, app.DisplayModes.Current[LgGdi]);
    }

    // A start after a crash between the record and the change, or during the change.
    [Fact]
    public void ARecordedChangeIsRestoredOnlyWhenItWasMade()
    {
        using var app = Fixture();
        app.DisplayModes.Current[LgGdi] = Lg720At60;
        Recovery(app).Save([
            // Recorded, not made: the Dell still shows its desktop mode.
            new DisplayModeRecoveryEntry(Dell, DellDesktop, Dell1080At144, null),
            // Recorded and made, not read back yet.
            new DisplayModeRecoveryEntry(Lg, LgDesktop, Lg720At60, null),
        ]);

        app.Services.Modes.OnDisplaysChanged();

        Assert.Equal([(LgGdi, LgDesktop)], app.DisplayModes.Applied);
        Assert.Equal(DellDesktop, app.DisplayModes.Current[DellGdi]);
        Assert.False(File.Exists(RecoveryPath(app)));
    }

    // A start after a crash while one profile's resolution followed another's.
    [Fact]
    public void ARecordedChangeBetweenProfilesIsRestoredFromEitherMode()
    {
        foreach (var shown in new[] { Dell960At120, Dell1080At144 })
        {
            using var app = Fixture();
            app.DisplayModes.Current[DellGdi] = shown;
            Recovery(app).Save([new DisplayModeRecoveryEntry(Dell, DellDesktop, Dell1080At144, Dell960At120)]);

            app.Services.Modes.OnDisplaysChanged();

            Assert.Equal(DellDesktop, app.DisplayModes.Current[DellGdi]);
            Assert.False(File.Exists(RecoveryPath(app)));
        }
    }

    // An unexpected failure on one monitor never stops the other monitor or the colors.
    [Fact]
    public void AnUnexpectedFailureNeitherStopsTheOtherMonitorNorTheColors()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, 1920, 1080);
        SetResolution(app.Game, Lg, 1280, 720);
        app.DisplayModes.Failure = gdi => gdi == DellGdi ? new InvalidOperationException("driver") : null;

        Focus(app, "game.exe");

        Assert.Equal(Lg720At60, app.DisplayModes.Current[LgGdi]);
        Assert.Equal(["Game", "Game"], app.AppliedProfiles());
        Assert.Equal("Game", app.Session.Switching.ActiveProfileName);
    }

    [Fact]
    public void AnUnexpectedFailureWhileRestoringNeitherStopsTheColorsNorLosesTheEntry()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, 1920, 1080);
        Focus(app, "game.exe");
        app.DisplayModes.Failure = _ => new InvalidOperationException("driver");
        var raised = 0;
        app.Services.Modes.ModesChanged += () => raised++;

        app.Automatic.SetGlobalEnabled(false);
        app.Services.Modes.OnDisplaysChanged();
        app.Services.Modes.RestoreOriginals();

        Assert.Equal("Windows", app.AppliedProfiles()[^1]);
        Assert.Single(Recovery(app).Load()!);
        // A failure that repeats requests no display refresh, so it cannot loop.
        Assert.Equal(0, raised);

        app.DisplayModes.Failure = null;
        app.Services.Modes.OnDisplaysChanged();
        Assert.Equal(DellDesktop, app.DisplayModes.Current[DellGdi]);
        Assert.False(File.Exists(RecoveryPath(app)));
    }

    [Fact]
    public void AnUnexpectedFailureWhileListingModesOffersNone()
    {
        using var app = Fixture();
        app.DisplayModes.Failure = _ => new InvalidOperationException("driver");

        Assert.Empty(app.Services.Modes.OfferedModes(app.Nvidia.Displays[0]));
    }

    [Fact]
    public void AnUnreadableRecoveryFileIsIgnored()
    {
        using var app = Fixture();
        Directory.CreateDirectory(Path.GetDirectoryName(RecoveryPath(app))!);
        File.WriteAllText(RecoveryPath(app), "{ not json");

        app.Services.Modes.OnDisplaysChanged();

        Assert.DoesNotContain(app.DisplayModes.Calls, call => call.StartsWith("apply", StringComparison.Ordinal));
    }

    [Fact]
    public void ATargetThatArrivesDuringAChangeIsCheckedAgainAfterIt()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, 1920, 1080);
        var rechecks = 0;
        app.Services.Modes.RecheckRequested += () => rechecks++;
        // A focus change handled while Windows changes the mode.
        app.DisplayModes.DuringApply = () =>
        {
            app.DisplayModes.DuringApply = null;
            app.Services.Modes.Apply(null);
        };

        Focus(app, "game.exe");

        Assert.Equal(1, rechecks);
        Assert.Single(app.DisplayModes.Applied);
    }

    [Fact]
    public void WithoutResolutionsTheBackendIsNeverReached()
    {
        using var app = Fixture();
        Recovery(app).Save([]);

        app.Services.Modes.OnDisplaysChanged();
        Focus(app, "game.exe");
        Focus(app, "notepad.exe");
        Focus(app, "explorer.exe");
        app.Automatic.ToggleWindowsOverride();
        app.Automatic.ResumeAutomatic();
        app.Automatic.SetGlobalEnabled(false);
        app.Automatic.SetGlobalEnabled(true);
        app.Services.Modes.OnDisplaysChanged();
        app.Services.Modes.RestoreOriginals();

        Assert.Empty(app.DisplayModes.Calls);
        Assert.Contains("Game", app.AppliedProfiles());
    }

    [Fact]
    public void OfferedModesUseTheDesktopModeWhileAProfileResolutionIsShown()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, 1280, 960);
        Focus(app, "game.exe");
        var dell = app.Nvidia.Displays.Single(display => display.MonitorId == Dell);

        var offered = app.Services.Modes.OfferedModes(dell);

        Assert.Equal([DellDesktop, Dell1080At144, Dell960At120], offered);
    }
}
