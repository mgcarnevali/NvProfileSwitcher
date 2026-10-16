using NvProfileSwitcher.Models;
using Xunit;
using static NvProfileSwitcher.Core.Tests.SwitchingFixture;

namespace NvProfileSwitcher.Core.Tests;

public sealed class DisplayColorControllerTests
{
    private static readonly ColorValues Previewed = new(90, 95, 1.2, 12, 30);

    [Fact]
    public void TheBaseProfileWritesTheSavedValuesOfEveryDisplay()
    {
        using var app = new SwitchingFixture();

        app.Colors.SetBase(app.Game, allDisplays: true);

        Assert.Equal(
            [new ColorApplication(Dell, 80, 105, 110, 1.15, 10), new ColorApplication(Lg, 60, 100, 100, 1, 355)],
            app.Backend.Applied);
    }

    [Fact]
    public void RestoreAppliesTheWindowsProfile()
    {
        using var app = new SwitchingFixture();

        app.Colors.RestoreWindows();

        Assert.Equal(["Windows", "Windows"], app.AppliedProfiles());
    }

    [Fact]
    public void DisplaysWithoutValuesAreSkipped()
    {
        using var app = new SwitchingFixture();
        app.Game.DisplayProfiles.RemoveAll(v => v.MonitorId == Dell);

        app.Colors.SetBase(app.Game, allDisplays: true);

        Assert.Equal([Lg], app.Backend.Applied.Select(a => a.MonitorId));
    }

    // Choosing the same base profile again writes nothing unless every display is asked for.
    [Fact]
    public void TheSameBaseProfileIsWrittenAgainOnlyWhenAsked()
    {
        using var app = new SwitchingFixture();
        app.Colors.SetBase(app.Game, allDisplays: false);
        app.Backend.Applied.Clear();

        app.Colors.SetBase(app.Game, allDisplays: false);
        Assert.Empty(app.Backend.Applied);

        app.Colors.SetBase(app.Game, allDisplays: true);
        Assert.Equal(["Game", "Game"], app.AppliedProfiles());
    }

    [Fact]
    public void NothingIsAppliedWhilePausedUnlessForced()
    {
        using var app = new SwitchingFixture();
        app.Session.Switching.GlobalPaused = true;

        app.Colors.SetBase(app.Game, allDisplays: true);
        app.Colors.SetPreview(Dell, Previewed);
        Assert.Empty(app.Backend.Applied);

        // A forced restore shows Windows on every display, the previewed one too.
        app.Colors.RestoreWindows(force: true);
        Assert.Equal(["Windows", "Windows"], app.AppliedProfiles());
    }

    [Fact]
    public void NothingIsAppliedWithoutTheApi()
    {
        using var app = new SwitchingFixture(initialize: false);

        app.Colors.RestoreWindows(force: true);

        Assert.Empty(app.Factory.Created);
    }

    // A display the driver rejects does not stop the others: its result is not checked.
    [Fact]
    public void RejectedDisplayDoesNotStopTheOthers()
    {
        using var app = new SwitchingFixture();
        app.Backend.RejectedMonitors.Add(Dell);

        app.Colors.SetBase(app.Game, allDisplays: true);

        Assert.Equal([Dell, Lg], app.Backend.Applied.Select(a => a.MonitorId));
    }

    // A display that throws does not keep the others from their values; the exception is
    // thrown once all were written (deliberate deviation: the original stopped at that display).
    [Fact]
    public void AFailedDisplayDoesNotStopTheOthersAndIsThrownAfterThem()
    {
        using var app = new SwitchingFixture();
        app.Backend.ApplyFailure = display => display.MonitorId == Dell ? new InvalidOperationException("NVAPI call failed") : null;

        Assert.Throws<InvalidOperationException>(() => app.Colors.SetBase(app.Game, allDisplays: true));

        Assert.Equal([Lg], app.Backend.Applied.Select(a => a.MonitorId));
        Assert.True(app.Colors.HasWritten(Dell));
    }

    [Fact]
    public void ABatchWritesEachDisplayOnceWithItsFinalValues()
    {
        using var app = new SwitchingFixture();

        using (app.Colors.Batch())
        {
            app.Colors.RestoreWindows();
            app.Colors.SetPreview(Dell, Previewed);
            using (app.Colors.Batch())
                app.Colors.SetBase(app.Game, allDisplays: true);
            Assert.Empty(app.Backend.Applied);
        }

        Assert.Equal([new ColorApplication(Dell, 12, 90, 95, 1.2, 30), new ColorApplication(Lg, 60, 100, 100, 1, 355)], app.Backend.Applied);
    }

    // Ending the preview writes only its display, with the base profile.
    [Fact]
    public void ThePreviewIsALayerOverTheBaseProfileOfItsDisplay()
    {
        using var app = new SwitchingFixture();
        app.Colors.SetBase(app.Game, allDisplays: true);
        app.Backend.Applied.Clear();

        app.Colors.SetPreview(Dell, Previewed);
        app.Colors.ClearPreview();

        Assert.Equal([new ColorApplication(Dell, 12, 90, 95, 1.2, 30), new ColorApplication(Dell, 80, 105, 110, 1.15, 10)], app.Backend.Applied);
    }

    [Fact]
    public void ABatchThatDoesNotThrowReportsAFailedWrite()
    {
        using var app = new SwitchingFixture();
        var failures = 0;
        app.Colors.WriteFailed += () => failures++;
        app.Backend.ApplyFailure = _ => new InvalidOperationException("NVAPI call failed");

        using (app.Colors.Batch())
            app.Colors.SetBase(app.Game, allDisplays: true);
        Assert.Equal(1, failures);

        Assert.Throws<InvalidOperationException>(() =>
        {
            using (app.Colors.Batch(throwOnFailure: true))
                app.Colors.SetBase(app.Game, allDisplays: true);
        });
        Assert.Equal(1, failures);
    }

    [Fact]
    public void OnlyTheDisplaysThatWereWrittenAreKnownAsWritten()
    {
        using var app = new SwitchingFixture();

        app.Colors.SetPreview(Lg, Previewed);

        Assert.True(app.Colors.HasWritten(Lg));
        Assert.False(app.Colors.HasWritten(Dell));
    }
}
