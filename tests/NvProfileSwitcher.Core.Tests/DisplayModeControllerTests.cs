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
        app.Displays.Refresh();
    }

    private static void Connect(SwitchingFixture app, ConnectedDisplay display)
    {
        app.Backend.Displays.Add(display);
        app.Displays.Refresh();
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

        Assert.Null(app.Services.Modes.ModeOptions(app.Displays.Displays[0]));
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
    public void ModeOptionsUseTheDesktopModeWhileAProfileResolutionIsShown()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, 1280, 960);
        Focus(app, "game.exe");
        var dell = app.Displays.Displays.Single(display => display.MonitorId == Dell);

        var options = app.Services.Modes.ModeOptions(dell)!;

        Assert.Equal(DellDesktop, options.Desktop);
        Assert.Equal([DellDesktop, Dell1080At144, Dell1080At60, Dell960At120], options.Supported);
    }

    // ---- Refresh rate, independent of the resolution ----

    private static void SetRate(ProfileListItem profile, string monitorId, RefreshRateSetting? rate) =>
        profile.DisplayProfiles.Single(values => values.MonitorId == monitorId).RefreshRate = rate;

    [Fact]
    public void ARateAloneChangesOnlyTheRateAndTheDesktopModeComesBack()
    {
        using var app = Fixture();
        app.DisplayModes.AddMonitor(LgGdi, LgDesktop, Lg720At60, new DisplayModeInfo(1920, 1080, 144));
        SetRate(app.Game, Lg, RefreshRateSetting.Explicit(144));

        Focus(app, "game.exe");
        Assert.Equal(new DisplayModeInfo(1920, 1080, 144), app.DisplayModes.Current[LgGdi]);
        Assert.DoesNotContain(app.DisplayModes.Calls, call => call.Contains(DellGdi));

        Focus(app, "explorer.exe");
        Assert.Equal(LgDesktop, app.DisplayModes.Current[LgGdi]);
        Assert.False(app.Services.Modes.OwnsAnyMonitor);
    }

    [Fact]
    public void AResolutionAndARateAreAppliedTogether()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, 1920, 1080);
        SetRate(app.Game, Dell, RefreshRateSetting.Explicit(60));

        Focus(app, "game.exe");

        Assert.Equal([(DellGdi, Dell1080At60)], app.DisplayModes.Applied);
        Focus(app, "explorer.exe");
        Assert.Equal(DellDesktop, app.DisplayModes.Current[DellGdi]);
    }

    [Fact]
    public void HighestAvailableUsesTheHighestRateOfTheResolution()
    {
        using var app = Fixture();
        app.DisplayModes.AddMonitor(DellGdi, new DisplayModeInfo(2560, 1440, 60), DellDesktop, Dell1080At144, Dell1080At60);
        SetResolution(app.Game, Dell, 1920, 1080);
        SetRate(app.Game, Dell, RefreshRateSetting.Highest);

        Focus(app, "game.exe");

        // Not the 60 Hz of the desktop.
        Assert.Equal(Dell1080At144, app.DisplayModes.Current[DellGdi]);
    }

    // An explicit rate the monitor no longer offers is not replaced by another one: the monitor
    // is left as it is, so no rate the user did not choose is applied.
    [Fact]
    public void AnExplicitRateNoLongerOfferedLeavesTheMonitorAsItIs()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, 1920, 1080);
        SetRate(app.Game, Dell, RefreshRateSetting.Explicit(165));

        Focus(app, "game.exe");

        Assert.Empty(app.DisplayModes.Applied);
        Assert.Equal(DellDesktop, app.DisplayModes.Current[DellGdi]);
        Assert.False(app.Services.Modes.OwnsAnyMonitor);
        Assert.Equal(["Game", "Game"], app.AppliedProfiles());
    }

    // Two profiles with the same resolution and different rates are different targets.
    [Fact]
    public void ADifferentRateIsADifferentTarget()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, 1920, 1080);
        SetResolution(app.Notepad, Dell, 1920, 1080);
        SetRate(app.Notepad, Dell, RefreshRateSetting.Explicit(60));

        Focus(app, "game.exe");
        Focus(app, "notepad.exe");

        Assert.Equal([Dell1080At144, Dell1080At60], app.DisplayModes.Applied.Select(a => a.Mode));
        Focus(app, "explorer.exe");
        Assert.Equal(DellDesktop, app.DisplayModes.Current[DellGdi]);
    }

    // The recovery records the full mode: a crash with only the rate changed restores it.
    [Fact]
    public void ARateChangeIsRecoveredAfterACrash()
    {
        using var app = Fixture();
        var desktopAt60 = new DisplayModeInfo(2560, 1440, 60);
        app.DisplayModes.AddMonitor(DellGdi, desktopAt60, DellDesktop);
        SetRate(app.Game, Dell, RefreshRateSetting.Highest);
        IReadOnlyList<DisplayModeRecoveryEntry>? duringChange = null;
        app.DisplayModes.DuringApply = () => duringChange = Recovery(app).Load();

        Focus(app, "game.exe");

        var entry = Assert.Single(duringChange!);
        Assert.Equal((desktopAt60, DellDesktop), (entry.Original, entry.Target));

        using var restarted = Fixture();
        restarted.DisplayModes.AddMonitor(DellGdi, DellDesktop, desktopAt60);
        Recovery(restarted).Save([new DisplayModeRecoveryEntry(Dell, desktopAt60, DellDesktop, DellDesktop)]);
        restarted.Services.Modes.OnDisplaysChanged();
        Assert.Equal(desktopAt60, restarted.DisplayModes.Current[DellGdi]);
    }

    // ---- What the driver reports (issue #41) ----

    // A monitor at 1920×1080 and 60 Hz that also offers 59 Hz, 1440×1080 at 60 and 59 Hz, and 120 Hz.
    private static readonly DisplayModeInfo Desktop60 = new(1920, 1080, 60);
    private static readonly DisplayModeInfo Desktop59 = new(1920, 1080, 59);
    private static readonly DisplayModeInfo Desktop120 = new(1920, 1080, 120);
    private static readonly DisplayModeInfo Stretched60 = new(1440, 1080, 60);
    private static readonly DisplayModeInfo Stretched59 = new(1440, 1080, 59);

    private static SwitchingFixture FixtureAt60()
    {
        var app = Fixture(withModes: false);
        app.DisplayModes.AddMonitor(DellGdi, Desktop60, Desktop59, Desktop120, Stretched60, Stretched59);
        return app;
    }

    // Windows accepts the change, but the read right after it still reports the previous mode;
    // the monitor shows the new one a moment later. It stays recorded and is restored on leaving.
    [Fact]
    public void AChangeReportedLateIsStillRestored()
    {
        using var app = FixtureAt60();
        SetResolution(app.Game, Dell, 1440, 1080);
        app.DisplayModes.DuringApply = () => app.DisplayModes.Current[DellGdi] = Desktop60;

        Focus(app, "game.exe");
        app.DisplayModes.DuringApply = null;
        app.DisplayModes.Current[DellGdi] = Stretched60;
        app.Services.Modes.OnDisplaysChanged();

        Assert.True(app.Services.Modes.OwnsAnyMonitor);
        Assert.True(File.Exists(RecoveryPath(app)));
        Focus(app, "explorer.exe");
        Assert.Equal(Desktop60, app.DisplayModes.Current[DellGdi]);
        Assert.False(app.Services.Modes.OwnsAnyMonitor);
    }

    // A change Windows rejects, which the read then reports as unchanged, is still not recorded.
    [Fact]
    public void ARejectedChangeIsStillNotRecorded()
    {
        using var app = FixtureAt60();
        SetResolution(app.Game, Dell, 1440, 1080);
        app.DisplayModes.Rejected.Add(DellGdi);

        Focus(app, "game.exe");

        Assert.Equal(Desktop60, app.DisplayModes.Current[DellGdi]);
        Assert.False(app.Services.Modes.OwnsAnyMonitor);
        Assert.False(File.Exists(RecoveryPath(app)));
    }

    // The mode applied is later reported 1 Hz off (a fractional rate rounded the other way): it is
    // still this application's, and leaving restores the desktop mode.
    [Fact]
    public void TheAppliedModeReportedOneHertzOffIsStillRestored()
    {
        using var app = FixtureAt60();
        SetResolution(app.Game, Dell, 1440, 1080);

        Focus(app, "game.exe");
        app.DisplayModes.Current[DellGdi] = Stretched59;
        app.Services.Modes.OnDisplaysChanged();
        Focus(app, "explorer.exe");

        Assert.Equal(Desktop60, app.DisplayModes.Current[DellGdi]);
        Assert.False(app.Services.Modes.OwnsAnyMonitor);
    }

    // 59 and 60 Hz are different choices: each is applied from the other and the desktop rate
    // comes back exactly.
    [Theory]
    [InlineData(60, 59)]
    [InlineData(59, 60)]
    public void AnExplicitRateOneHertzFromTheDesktopIsAppliedAndRestored(int desktopRate, int profileRate)
    {
        using var app = Fixture(withModes: false);
        var desktop = new DisplayModeInfo(1920, 1080, desktopRate);
        var chosen = new DisplayModeInfo(1920, 1080, profileRate);
        app.DisplayModes.AddMonitor(DellGdi, desktop, chosen);
        SetRate(app.Game, Dell, RefreshRateSetting.Explicit(profileRate));

        Focus(app, "game.exe");
        Assert.Equal(chosen, app.DisplayModes.Current[DellGdi]);

        Focus(app, "explorer.exe");
        Assert.Equal(desktop, app.DisplayModes.Current[DellGdi]);
        Assert.Equal([chosen, desktop], app.DisplayModes.Applied.Select(applied => applied.Mode));
        Assert.False(app.Services.Modes.OwnsAnyMonitor);
    }

    // A profile that changed only the rate by 1 Hz (59 over a 60 Hz desktop), and a driver that
    // reports the applied rate rounded to the desktop one: leaving applies the exact desktop mode
    // all the same. The monitor already reports it, so no change is requested and no display
    // refresh follows: a later display change finds nothing to do.
    [Fact]
    public void AnAppliedRateReportedAsTheDesktopOneIsRestoredWithoutALoop()
    {
        using var app = FixtureAt60();
        SetRate(app.Game, Dell, RefreshRateSetting.Explicit(59));
        var raised = 0;
        app.Services.Modes.ModesChanged += () => raised++;

        Focus(app, "game.exe");
        Assert.Equal(1, raised);
        app.DisplayModes.Current[DellGdi] = Desktop60;
        Focus(app, "explorer.exe");

        Assert.Equal([Desktop59, Desktop60], app.DisplayModes.Applied.Select(applied => applied.Mode));
        Assert.False(app.Services.Modes.OwnsAnyMonitor);
        Assert.False(File.Exists(RecoveryPath(app)));
        Assert.Equal(1, raised);

        app.Services.Modes.OnDisplaysChanged();
        app.Services.Modes.OnDisplaysChanged();
        Assert.Equal(2, app.DisplayModes.Applied.Count);
        Assert.Equal(1, raised);
    }

    // The same restore, rejected by Windows: the monitor still reports the desktop rate, but the
    // entry is kept, in memory and in the recovery file, and the next display change restores it.
    [Fact]
    public void ARejectedRestoreOfAnAmbiguousRateKeepsTheEntry()
    {
        using var app = FixtureAt60();
        SetRate(app.Game, Dell, RefreshRateSetting.Explicit(59));
        Focus(app, "game.exe");
        app.DisplayModes.Current[DellGdi] = Desktop60;
        app.DisplayModes.Rejected.Add(DellGdi);

        Focus(app, "explorer.exe");
        Assert.True(app.Services.Modes.OwnsAnyMonitor);
        Assert.Single(Recovery(app).Load()!);

        app.DisplayModes.Rejected.Clear();
        app.Services.Modes.OnDisplaysChanged();
        Assert.Equal([Desktop59, Desktop60], app.DisplayModes.Applied.Select(applied => applied.Mode));
        Assert.False(app.Services.Modes.OwnsAnyMonitor);
        Assert.False(File.Exists(RecoveryPath(app)));
    }

    // A monitor back at its desktop mode after a change of resolution is not ambiguous: nothing is
    // applied again.
    [Fact]
    public void AMonitorBackAtItsDesktopModeAfterAResolutionIsNotChangedAgain()
    {
        using var app = FixtureAt60();
        SetResolution(app.Game, Dell, 1440, 1080);
        Focus(app, "game.exe");

        app.DisplayModes.Current[DellGdi] = Desktop60;
        Focus(app, "explorer.exe");

        Assert.Equal([Stretched60], app.DisplayModes.Applied.Select(applied => applied.Mode));
        Assert.False(app.Services.Modes.OwnsAnyMonitor);
    }

    // ---- Monitor identity changes (H2) ----

    private const string TwinGdi = @"\\.\DISPLAY3";

    private static string ConnectionOf(int number) => $"DEL4321@{FakeDisplayBackend.Instance(number)}";

    // A second monitor with the same serial number, found after the Dell's mode was changed: both
    // take their connection identity. The Dell's entry follows it by the connection recorded with
    // it, so the Dell is restored, and the other monitor is never written.
    [Fact]
    public void AMonitorWhoseSerialIsFoundSharedLaterIsStillRestored()
    {
        using var app = Fixture();
        app.DisplayModes.AddMonitor(TwinGdi, DellDesktop, Dell1080At144);
        SetResolution(app.Game, Dell, 1920, 1080);
        Focus(app, "game.exe");
        Assert.Equal(ConnectionOf(1), Assert.Single(Recovery(app).Load()!).Connection);

        Connect(app, FakeDisplayBackend.Display(Dell, "DELL U2720Q", 3));
        Assert.DoesNotContain(app.Services.Displays.Displays, display => display.MonitorId == Dell);
        app.Services.Modes.OnDisplaysChanged();
        Focus(app, "explorer.exe");

        Assert.Equal(DellDesktop, app.DisplayModes.Current[DellGdi]);
        Assert.Equal([(DellGdi, Dell1080At144), (DellGdi, DellDesktop)], app.DisplayModes.Applied);
        Assert.DoesNotContain(app.DisplayModes.Calls, call => call.StartsWith($"apply {TwinGdi}", StringComparison.Ordinal));
        Assert.False(app.Services.Modes.OwnsAnyMonitor);
        Assert.False(File.Exists(RecoveryPath(app)));
    }

    // A previous session changed the monitor on \\.\DISPLAY3 while its serial number was its own;
    // both monitors with that serial number are connected now. Only that one is restored, even
    // with the other one showing the same mode.
    [Fact]
    public void AnEntryFromAPreviousSessionFollowsItsConnectionOnly()
    {
        using var app = Fixture();
        app.DisplayModes.AddMonitor(TwinGdi, DellDesktop, Dell1080At144);
        Connect(app, FakeDisplayBackend.Display(Dell, "DELL U2720Q", 3));
        app.DisplayModes.Current[DellGdi] = Dell1080At144;
        app.DisplayModes.Current[TwinGdi] = Dell1080At144;
        Recovery(app).Save([new DisplayModeRecoveryEntry(Dell, DellDesktop, Dell1080At144, Dell1080At144, ConnectionOf(3))]);

        app.Services.Modes.OnDisplaysChanged();

        Assert.Equal([(TwinGdi, DellDesktop)], app.DisplayModes.Applied);
        Assert.Equal(Dell1080At144, app.DisplayModes.Current[DellGdi]);
        Assert.False(File.Exists(RecoveryPath(app)));
    }

    // The Dell is replaced on the same port while its entry is open, by a monitor of the same
    // model without a serial number (whose connection identity is the one recorded) or with
    // another one. Neither takes the entry, even showing the mode the Dell had: nothing is written.
    [Theory]
    [InlineData("DEL4321")]
    [InlineData("DEL4321-XYZ999")]
    public void AMonitorReplacedOnTheSamePortNeverTakesTheEntry(string replacementId)
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, 1920, 1080);
        Focus(app, "game.exe");

        Disconnect(app, Dell);
        Connect(app, FakeDisplayBackend.Display(replacementId, "DELL U2720Q", 1));
        app.DisplayModes.Current[DellGdi] = Dell1080At144;
        app.Services.Modes.OnDisplaysChanged();
        Focus(app, "explorer.exe");
        app.Services.Modes.RestoreOriginals();

        Assert.Equal([(DellGdi, Dell1080At144)], app.DisplayModes.Applied);
        Assert.True(app.Services.Modes.OwnsAnyMonitor);
        Assert.Equal(Dell, Assert.Single(Recovery(app).Load()!).MonitorId);
    }

    // A recovery file written without the connection (an earlier development build) is read, and
    // its entries are restored by monitor id as before.
    [Fact]
    public void ARecoveryFileWithoutConnectionsIsStillRead()
    {
        using var app = Fixture();
        app.DisplayModes.Current[DellGdi] = Dell1080At144;
        Recovery(app).Save([new DisplayModeRecoveryEntry(Dell, DellDesktop, Dell1080At144, Dell1080At144)]);
        var json = File.ReadAllText(RecoveryPath(app));
        File.WriteAllText(RecoveryPath(app), System.Text.RegularExpressions.Regex.Replace(json, @",?\s*""Connection""\s*:\s*null", string.Empty));
        Assert.DoesNotContain("Connection", File.ReadAllText(RecoveryPath(app)));

        app.Services.Modes.OnDisplaysChanged();

        Assert.Equal(DellDesktop, app.DisplayModes.Current[DellGdi]);
        Assert.False(File.Exists(RecoveryPath(app)));
    }

    // A rate the user sets while the profile is active, more than 1 Hz from the applied one, is
    // theirs: leaving does not restore over it.
    [Fact]
    public void ARateTheUserSetIsNeverOverwritten()
    {
        using var app = FixtureAt60();
        SetRate(app.Game, Dell, RefreshRateSetting.Explicit(59));

        Focus(app, "game.exe");
        app.DisplayModes.Current[DellGdi] = Desktop120;
        app.Services.Modes.OnDisplaysChanged();
        Focus(app, "explorer.exe");

        Assert.Equal(Desktop120, app.DisplayModes.Current[DellGdi]);
        Assert.False(app.Services.Modes.OwnsAnyMonitor);
        Assert.False(File.Exists(RecoveryPath(app)));
    }

    [Fact]
    public void WithoutResolutionsOrRatesTheBackendIsNeverReachedEvenWithTheWindowsProfileRate()
    {
        using var app = Fixture();
        // A rate on the Windows profile is never applied: it is not a profile target.
        app.Windows.DisplayProfiles[0].RefreshRate = RefreshRateSetting.Highest;

        Focus(app, "game.exe");
        Focus(app, "explorer.exe");

        Assert.Empty(app.DisplayModes.Calls);
    }
}
