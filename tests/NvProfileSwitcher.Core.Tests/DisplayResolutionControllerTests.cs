using NvProfileSwitcher.Services;
using Xunit;
using static NvProfileSwitcher.Core.Tests.SwitchingFixture;

namespace NvProfileSwitcher.Core.Tests;

// Resolutions over the switching services: the Dell is NVIDIA display 1 (2560×1440, also
// 1920×1080 and 1280×960) and the LG display 2 (1920×1080, also 1280×720). Each resolution change
// gives the displays new NVIDIA handles, as the driver may, so a color written with the handles
// read before the change is told apart from one written after the refresh.
public sealed class DisplayResolutionControllerTests
{
    private static readonly DisplayResolution DellDesktop = new(2560, 1440);
    private static readonly DisplayResolution Dell1080 = new(1920, 1080);
    private static readonly DisplayResolution Dell960 = new(1280, 960);
    private static readonly DisplayResolution LgDesktop = new(1920, 1080);
    private static readonly DisplayResolution Lg720 = new(1280, 720);

    private static SwitchingFixture Fixture()
    {
        var app = new SwitchingFixture();
        var modes = app.Factory.Modes;
        modes.Add(1, DellDesktop, Dell1080, Dell960);
        modes.Add(2, LgDesktop, Lg720);
        modes.AfterChange = () =>
        {
            var displays = app.Backend.Displays;
            for (var i = 0; i < displays.Count; i++)
                displays[i] = displays[i] with { Handle = displays[i].Handle + 100 };
        };
        return app;
    }

    private static void SetResolution(ProfileListItem profile, string monitorId, DisplayResolution? resolution) =>
        profile.DisplayProfiles.Single(values => values.MonitorId == monitorId).Resolution = resolution;

    private static void Focus(SwitchingFixture app, string executable)
    {
        app.Foreground = executable;
        app.Active.Check();
    }

    // The display refresh, in the order DisplayRefreshCoordinator.Refresh runs it.
    private static void Refresh(SwitchingFixture app)
    {
        using (app.Colors.Batch())
        {
            app.Nvidia.RefreshDisplays();
            app.Colors.RefreshAll(app.Resolutions.OnDisplaysChanged());
        }
    }

    private static List<string> Timeline(SwitchingFixture app) => app.Factory.Modes.Timeline;

    private static string RecoveryPath(SwitchingFixture app) =>
        Path.Combine(Path.GetDirectoryName(app.Session.ConfigPath)!, "display-modes.json");

    private static ColorApplication? LastColors(SwitchingFixture app, string monitorId) => app.Backend.Current(monitorId);

    [Fact]
    public void AProfileResolutionIsAppliedAndItsColorsAreWrittenByTheRefreshWithTheNewHandles()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, Dell1080);

        Focus(app, "game.exe");

        Assert.Equal([(1u, Dell1080)], app.Factory.Modes.Changes);
        // During the change only the LG, whose resolution did not change, is written.
        Assert.Equal(["resolution 1 1920x1080", $"colors {Lg} handle 2"], Timeline(app));
        Assert.True(app.Resolutions.OwnsAnyMonitor);

        Refresh(app);

        Assert.Equal($"colors {Dell} handle 101", Timeline(app).First(entry => entry.StartsWith($"colors {Dell}", StringComparison.Ordinal)));
        Assert.True(LastColors(app, Dell)!.Value.Matches(app.Game.DisplayProfiles[0]));
    }

    // A profile with colors only behaves as before resolutions existed: every display at once.
    [Fact]
    public void AProfileWithColorsOnlyIsWrittenAtOnceAndChangesNoResolution()
    {
        using var app = Fixture();

        Focus(app, "game.exe");

        Assert.Empty(app.Factory.Modes.Changes);
        Assert.Equal([$"colors {Dell} handle 1", $"colors {Lg} handle 2"], Timeline(app));
        Assert.False(app.Resolutions.OwnsAnyMonitor);
        Assert.False(File.Exists(RecoveryPath(app)));
    }

    [Fact]
    public void LeavingTheProfileRestoresTheDesktopResolution()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, Dell1080);
        Focus(app, "game.exe");
        Refresh(app);

        Focus(app, "explorer.exe");

        Assert.Equal([(1u, Dell1080), (1u, DellDesktop)], app.Factory.Modes.Changes);
        Assert.False(app.Resolutions.OwnsAnyMonitor);
        Assert.False(File.Exists(RecoveryPath(app)));
        // The Windows colors of the Dell wait for the refresh of that change.
        Assert.True(LastColors(app, Dell)!.Value.Matches(app.Game.DisplayProfiles[0]));
        Refresh(app);
        Assert.True(LastColors(app, Dell)!.Value.Matches(app.Windows.DisplayProfiles[0]));
    }

    [Fact]
    public void AResolutionTheUserSetMeanwhileIsNeverOverwritten()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, Dell1080);
        Focus(app, "game.exe");

        app.Factory.Modes.Current[1] = Dell960;
        Focus(app, "explorer.exe");

        Assert.Equal(Dell960, app.Factory.Modes.Current[1]);
        Assert.Single(app.Factory.Modes.Changes);
        Assert.False(app.Resolutions.OwnsAnyMonitor);
        Assert.False(File.Exists(RecoveryPath(app)));
    }

    [Fact]
    public void SwitchingBetweenProfilesKeepsTheDesktopResolutionToRestore()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, Dell1080);
        SetResolution(app.Notepad, Dell, Dell960);

        Focus(app, "game.exe");
        Focus(app, "notepad.exe");
        Focus(app, "explorer.exe");

        Assert.Equal([(1u, Dell1080), (1u, Dell960), (1u, DellDesktop)], app.Factory.Modes.Changes);
        Assert.False(app.Resolutions.OwnsAnyMonitor);
    }

    [Fact]
    public void AChangeTheDriverRejectsChangesNothingAndTheColorsAreWrittenAtOnce()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, Dell1080);
        app.Factory.Modes.Rejected.Add(1);

        Focus(app, "game.exe");

        Assert.Empty(app.Factory.Modes.Changes);
        Assert.False(app.Resolutions.OwnsAnyMonitor);
        Assert.False(File.Exists(RecoveryPath(app)));
        Assert.Equal([$"colors {Dell} handle 1", $"colors {Lg} handle 2"], Timeline(app));
    }

    [Fact]
    public void TheSameTargetChangesNothingAgain()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, Dell1080);
        Focus(app, "game.exe");

        app.Active.ReapplyRealColors();
        app.Active.Reevaluate();

        Assert.Single(app.Factory.Modes.Changes);
    }

    // While the profile is active, a resolution the user sets is not fought: applying the same
    // profile again (the window activated, a preview ended) changes nothing.
    [Fact]
    public void ApplyingTheSameProfileAgainKeepsAResolutionTheUserSet()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, Dell1080);
        Focus(app, "game.exe");

        app.Factory.Modes.Current[1] = Dell960;
        app.Active.ReapplyRealColors();

        Assert.Equal(Dell960, app.Factory.Modes.Current[1]);
        Assert.Single(app.Factory.Modes.Changes);
    }

    // A held display is not written by any other write before its refresh: never with the
    // handles read before its resolution changed.
    [Fact]
    public void AHeldDisplayWaitsForItsRefresh()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, Dell1080);
        Focus(app, "game.exe");

        app.Colors.SetBase(app.Game, allDisplays: true);

        Assert.DoesNotContain(Timeline(app), entry => entry.StartsWith($"colors {Dell}", StringComparison.Ordinal));
    }

    [Fact]
    public void TheChangeIsRecordedBeforeItIsMade()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, Dell1080);
        var recordedBefore = false;
        app.Factory.Modes.AfterChange = () => recordedBefore = File.Exists(RecoveryPath(app));

        Focus(app, "game.exe");

        Assert.True(recordedBefore);
        var entry = Assert.Single(new ResolutionRecoveryStore(RecoveryPath(app)).Load());
        Assert.Equal(new ResolutionRecoveryEntry(Dell, DellDesktop, Dell1080), entry);
    }

    [Fact]
    public void AChangeThatCannotBeRecordedIsNotMade()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, Dell1080);
        // A directory where the file should be: it cannot be written.
        Directory.CreateDirectory(RecoveryPath(app));

        Focus(app, "game.exe");

        Assert.Empty(app.Factory.Modes.Changes);
        Assert.False(app.Resolutions.OwnsAnyMonitor);
    }

    // A start after a crash: the monitor still at the resolution applied is restored, one the user
    // changed since is left alone, and one not connected keeps its entry.
    [Fact]
    public void TheFirstRefreshRestoresWhatAPreviousSessionLeftChanged()
    {
        using var app = Fixture();
        app.Factory.Modes.Current[1] = Dell1080;
        app.Factory.Modes.Current[2] = LgDesktop;
        new ResolutionRecoveryStore(RecoveryPath(app)).Save([
            new ResolutionRecoveryEntry(Dell, DellDesktop, Dell1080),
            new ResolutionRecoveryEntry(Lg, LgDesktop, Lg720),
            new ResolutionRecoveryEntry("ACR0001-XYZ", new DisplayResolution(3840, 2160), new DisplayResolution(1920, 1080)),
        ]);

        Refresh(app);

        Assert.Equal([(1u, DellDesktop)], app.Factory.Modes.Changes);
        Assert.Equal("ACR0001-XYZ", Assert.Single(new ResolutionRecoveryStore(RecoveryPath(app)).Load()).MonitorId);
        // The restored Dell is written by the next refresh, with the handles it enumerates.
        Assert.DoesNotContain(Timeline(app), entry => entry.StartsWith($"colors {Dell}", StringComparison.Ordinal));
        Refresh(app);
        Assert.Contains($"colors {Dell} handle 101", Timeline(app));
    }

    [Fact]
    public void GlobalSwitchingOffRestoresAndItsWindowsColorsAreWrittenByTheRefresh()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, Dell1080);
        app.Automatic.Start();
        Focus(app, "game.exe");
        Refresh(app);

        app.Automatic.SetGlobalEnabled(false);

        Assert.Equal(DellDesktop, app.Factory.Modes.Current[1]);
        Assert.True(LastColors(app, Dell)!.Value.Matches(app.Game.DisplayProfiles[0]));
        // Written by the refresh even while paused: the held display waits for exactly that write.
        Refresh(app);
        Assert.True(LastColors(app, Dell)!.Value.Matches(app.Windows.DisplayProfiles[0]));
    }

    [Fact]
    public void TheWindowsOverrideRestoresTheDesktopResolution()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, Dell1080);
        app.Automatic.Start();
        Focus(app, "game.exe");

        app.Automatic.ToggleWindowsOverride();

        Assert.Equal(DellDesktop, app.Factory.Modes.Current[1]);
        Assert.False(app.Resolutions.OwnsAnyMonitor);
    }

    // Closing restores and writes at once: no display refresh follows.
    [Fact]
    public void ClosingRestoresAndWritesTheWindowsColorsAtOnce()
    {
        using var app = Fixture();
        SetResolution(app.Game, Dell, Dell1080);
        Focus(app, "game.exe");

        app.Resolutions.RestoreOriginals();
        app.Colors.ReleaseHeld();
        app.Colors.RestoreWindows();

        Assert.Equal(DellDesktop, app.Factory.Modes.Current[1]);
        Assert.True(LastColors(app, Dell)!.Value.Matches(app.Windows.DisplayProfiles[0]));
        Assert.False(File.Exists(RecoveryPath(app)));
    }
}
