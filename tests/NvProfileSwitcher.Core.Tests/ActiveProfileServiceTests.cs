using NvProfileSwitcher.Models;
using Xunit;
using static NvProfileSwitcher.Core.Tests.SwitchingFixture;

namespace NvProfileSwitcher.Core.Tests;

public sealed class ActiveProfileServiceTests
{
    [Fact]
    public void CheckAppliesAndPublishesTheProfileOfTheForegroundApplication()
    {
        using var app = new SwitchingFixture { Foreground = "game.exe" };

        app.Active.Check();

        Assert.Equal(["Game", "Game"], app.AppliedProfiles());
        Assert.Equal("Game", app.Session.Switching.ActiveProfileName);
        Assert.Equal("Game", app.StatusText);
    }

    [Fact]
    public void CheckMatchesAProfileWithoutExecutablesByItsName()
    {
        using var app = new SwitchingFixture { Foreground = "NOTEPAD.EXE" };

        app.Active.Check();

        Assert.Equal("notepad.exe", app.Session.Switching.ActiveProfileName);
    }

    [Fact]
    public void CheckDoesNothingWhenTheTargetIsAlreadyActive()
    {
        using var app = new SwitchingFixture { Foreground = "explorer.exe" };

        app.Active.Check();

        Assert.Empty(app.Backend.Applied);
        Assert.Equal(ActiveProfileDisplay.Initial, app.Session.Switching.DisplayedActiveProfile);
    }

    [Fact]
    public void InvalidatedCheckAppliesWindowsAgain()
    {
        using var app = new SwitchingFixture { Foreground = "explorer.exe" };

        app.Active.Invalidate();
        app.Active.Check();

        Assert.Equal(["Windows", "Windows"], app.AppliedProfiles());
        Assert.Equal("Windows", app.Session.Switching.ActiveProfileName);
    }

    [Fact]
    public void CheckDoesNothingWithoutTheApiOrDisplays()
    {
        using var withoutApi = new SwitchingFixture(initialize: false) { Foreground = "game.exe" };
        withoutApi.Active.Check();
        Assert.Equal("Windows", withoutApi.Session.Switching.ActiveProfileName);

        using var withoutDisplays = new SwitchingFixture(initialize: false) { Foreground = "game.exe" };
        withoutDisplays.Factory.Configure = null;
        withoutDisplays.Nvidia.Initialize();
        withoutDisplays.Nvidia.RefreshDisplays();
        withoutDisplays.Active.Check();
        Assert.Equal("Windows", withoutDisplays.Session.Switching.ActiveProfileName);
    }

    // A profile override whose profile is disabled returns to Automatic, without saving.
    [Fact]
    public void CheckReturnsAnInvalidProfileOverrideToAutomatic()
    {
        using var app = new SwitchingFixture { Foreground = "game.exe" };
        app.Session.Switching.OverrideMode = OverrideMode.Profile;
        app.Session.Switching.OverrideProfileIndex = 1;

        app.Active.Check();

        Assert.Equal(OverrideMode.Automatic, app.Session.Switching.OverrideMode);
        Assert.Equal(-1, app.Session.Switching.OverrideProfileIndex);
        Assert.Equal("Game", app.Session.Switching.ActiveProfileName);
        Assert.False(File.Exists(app.Session.ConfigPath));
    }

    [Fact]
    public void CheckAppliesAValidProfileOverride()
    {
        using var app = new SwitchingFixture { Foreground = "explorer.exe" };
        app.Session.Switching.OverrideMode = OverrideMode.Profile;
        app.Session.Switching.OverrideProfileIndex = 2;

        app.Active.Check();

        Assert.Equal(["notepad.exe", "notepad.exe"], app.AppliedProfiles());
        Assert.Equal("notepad.exe (override)", app.StatusText);
    }

    // A failed application during a check is ignored: the active profile is not updated, so
    // the next check tries again, and the colors stay partially applied until then.
    [Fact]
    public void FailedCheckKeepsTheActiveProfileAndPartialColors()
    {
        using var app = new SwitchingFixture { Foreground = "game.exe" };
        app.Backend.ApplyFailure = display => display.MonitorId == Lg ? new InvalidOperationException("NVAPI call failed") : null;

        app.Active.Check();

        Assert.Equal(["Game"], app.AppliedProfiles());
        Assert.Equal("Windows", app.Session.Switching.ActiveProfileName);
        Assert.Equal(ActiveProfileDisplay.Initial, app.Session.Switching.DisplayedActiveProfile);

        app.Backend.ApplyFailure = null;
        app.Active.Check();
        Assert.Equal(["Game", "Game", "Game"], app.AppliedProfiles());
        Assert.Equal("Game", app.Session.Switching.ActiveProfileName);
    }

    [Fact]
    public void ReapplyAppliesTheAutomaticMatchOrWindows()
    {
        using var app = new SwitchingFixture { Foreground = "game.exe" };
        app.Active.ReapplyRealColors();
        Assert.Equal(["Game", "Game"], app.AppliedProfiles());
        Assert.Equal("Game", app.Session.Switching.ActiveProfileName);

        app.Foreground = "explorer.exe";
        app.Active.ReapplyRealColors();
        Assert.Equal(["Game", "Game", "Windows", "Windows"], app.AppliedProfiles());
        Assert.Equal("Windows", app.StatusText);
    }

    // Reapplying always applies, even when the target is already the active profile.
    [Fact]
    public void ReapplyAppliesEvenWhenTheTargetIsActive()
    {
        using var app = new SwitchingFixture();

        app.Active.ReapplyRealColors();

        Assert.Equal(["Windows", "Windows"], app.AppliedProfiles());
    }

    [Fact]
    public void ReapplyAppliesADisabledProfileOverride()
    {
        using var app = new SwitchingFixture();
        app.Session.Switching.OverrideMode = OverrideMode.Profile;
        app.Session.Switching.OverrideProfileIndex = 1;

        app.Active.ReapplyRealColors();

        Assert.Equal(["Tarkov", "Tarkov"], app.AppliedProfiles());
        Assert.Equal("Tarkov (override)", app.StatusText);
        Assert.Equal(OverrideMode.Profile, app.Session.Switching.OverrideMode);
    }

    // Unlike a check, an invalid profile override applies Windows and keeps the override.
    [Fact]
    public void ReapplyAppliesWindowsForAnInvalidProfileOverride()
    {
        using var app = new SwitchingFixture { Foreground = "game.exe" };
        app.Session.Switching.OverrideMode = OverrideMode.Profile;
        app.Session.Switching.OverrideProfileIndex = 5;

        app.Active.ReapplyRealColors();

        Assert.Equal(["Windows", "Windows"], app.AppliedProfiles());
        Assert.Equal(OverrideMode.Profile, app.Session.Switching.OverrideMode);
        Assert.Equal("Windows (override)", app.StatusText);
    }

    [Fact]
    public void ReapplyIgnoresFailures()
    {
        using var app = new SwitchingFixture { Foreground = "game.exe" };
        app.Backend.ApplyFailure = _ => new InvalidOperationException("NVAPI call failed");

        app.Active.ReapplyRealColors();

        Assert.Equal("Windows", app.Session.Switching.ActiveProfileName);
    }

    [Fact]
    public void PublishTakesASnapshotOfTheSwitchingState()
    {
        using var app = new SwitchingFixture();
        var switching = app.Session.Switching;
        switching.ActiveProfileName = "Game";
        switching.OverrideMode = OverrideMode.Profile;

        app.Active.PublishActiveProfile();
        switching.ActiveProfileName = "notepad.exe";

        Assert.Equal(new ActiveProfileDisplay("Game", OverrideMode.Profile, false), switching.DisplayedActiveProfile);
        Assert.Equal("Game (override)", app.StatusText);
    }

    // The reapplication reads the foreground application only in Automatic mode, as the
    // original did; a check always reads it.
    [Fact]
    public void ReapplyReadsTheForegroundOnlyInAutomaticMode()
    {
        using var app = new SwitchingFixture();
        var switching = app.Session.Switching;

        switching.OverrideMode = OverrideMode.Windows;
        app.Active.ReapplyRealColors();
        switching.OverrideMode = OverrideMode.Profile;
        switching.OverrideProfileIndex = 0;
        app.Active.ReapplyRealColors();
        switching.OverrideProfileIndex = 9;
        app.Active.ReapplyRealColors();
        Assert.Equal(0, app.ForegroundReads);

        switching.OverrideMode = OverrideMode.Automatic;
        switching.OverrideProfileIndex = -1;
        app.Active.ReapplyRealColors();
        Assert.Equal(1, app.ForegroundReads);

        app.Active.Check();
        Assert.Equal(2, app.ForegroundReads);
    }
}
