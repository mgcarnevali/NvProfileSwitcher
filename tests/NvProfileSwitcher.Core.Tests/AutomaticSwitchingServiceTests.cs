using NvProfileSwitcher.Models;
using Xunit;

namespace NvProfileSwitcher.Core.Tests;

public sealed class AutomaticSwitchingServiceTests
{
    [Fact]
    public void StartCreatesTheMonitorOnceAndStartsIt()
    {
        using var app = new SwitchingFixture();
        Assert.False(app.Automatic.IsSwitchingActive);

        app.Automatic.Start();
        app.Automatic.Start();

        Assert.Equal(1, app.Monitor.StartCount);
        Assert.True(app.Automatic.IsSwitchingActive);
    }

    [Fact]
    public void StartDoesNotStartTheMonitorWhilePaused()
    {
        using var app = new SwitchingFixture();
        app.Session.Switching.GlobalPaused = true;

        app.Automatic.Start();

        Assert.Equal(0, app.Monitor.StartCount);
        Assert.False(app.Automatic.IsSwitchingActive);
    }

    [Fact]
    public void ForegroundChangesCheckTheActiveProfile()
    {
        using var app = new SwitchingFixture { Foreground = "game.exe" };
        app.Automatic.Start();

        app.Monitor.RaiseChanged();

        Assert.Equal("Game", app.Session.Switching.ActiveProfileName);
    }

    // Before the window first opens the monitor does not exist yet in the original, so
    // stopping, disposing or turning global switching on does not touch it.
    [Fact]
    public void MonitorIsUntouchedBeforeItIsCreated()
    {
        using var app = new SwitchingFixture();
        app.Session.Switching.GlobalPaused = true;

        app.Automatic.SetGlobalEnabled(true);
        app.Automatic.Stop();
        app.Automatic.Dispose();

        Assert.Equal((0, 0, 0), (app.Monitor.StartCount, app.Monitor.StopCount, app.Monitor.DisposeCount));
    }

    [Fact]
    public void GlobalOffStopsSwitchingAndForcesWindows()
    {
        using var app = new SwitchingFixture { Foreground = "game.exe" };
        app.Automatic.Start();
        app.Monitor.RaiseChanged();

        app.Automatic.SetGlobalEnabled(false);

        Assert.True(app.Session.Switching.GlobalPaused);
        Assert.False(app.Monitor.Running);
        Assert.Equal(["Game", "Game", "Windows", "Windows"], app.AppliedProfiles());
        Assert.Equal("Windows", app.StatusText);
        Assert.False(app.Automatic.IsSwitchingActive);
    }

    [Fact]
    public void GlobalOnStartsSwitchingAndReappliesTheRealColors()
    {
        using var app = new SwitchingFixture { Foreground = "game.exe" };
        app.Session.Switching.GlobalPaused = true;
        app.Automatic.Start();

        app.Automatic.SetGlobalEnabled(true);

        Assert.True(app.Monitor.Running);
        Assert.Equal(["Game", "Game"], app.AppliedProfiles());
        Assert.Equal("Game", app.StatusText);
    }

    [Fact]
    public void GlobalSwitchingIsSavedOnlyWhenRemembered()
    {
        using var forgotten = new SwitchingFixture();
        forgotten.Automatic.SetGlobalEnabled(false);
        Assert.False(File.Exists(forgotten.Session.ConfigPath));

        using var remembered = new SwitchingFixture();
        remembered.Session.Settings.RememberGlobalSwitching = true;
        remembered.Automatic.SetGlobalEnabled(false);
        Assert.Contains("\"GlobalSwitchingEnabled\": false", File.ReadAllText(remembered.Session.ConfigPath));
    }

    [Fact]
    public void SettingTheCurrentGlobalStateDoesNothing()
    {
        using var app = new SwitchingFixture();
        app.Session.Settings.RememberGlobalSwitching = true;

        app.Automatic.SetGlobalEnabled(true);

        Assert.Empty(app.Backend.Applied);
        Assert.False(File.Exists(app.Session.ConfigPath));
    }

    [Fact]
    public void WindowsOverrideAppliesWindowsAndBackToAutomatic()
    {
        using var app = new SwitchingFixture { Foreground = "game.exe" };
        app.Active.Check();

        app.Automatic.ToggleWindowsOverride();
        Assert.Equal(OverrideMode.Windows, app.Session.Switching.OverrideMode);
        Assert.Equal("Windows (override)", app.StatusText);

        app.Automatic.ToggleWindowsOverride();
        Assert.Equal(OverrideMode.Automatic, app.Session.Switching.OverrideMode);
        Assert.Equal(["Game", "Game", "Windows", "Windows", "Game", "Game"], app.AppliedProfiles());
        Assert.Equal("Game", app.StatusText);
    }

    [Fact]
    public void ProfileOverrideAppliesTheProfileAndResumeReturnsToAutomatic()
    {
        using var app = new SwitchingFixture { Foreground = "explorer.exe" };

        app.Automatic.ToggleProfileOverride(2);
        Assert.Equal((OverrideMode.Profile, 2), (app.Session.Switching.OverrideMode, app.Session.Switching.OverrideProfileIndex));
        Assert.Equal("notepad.exe (override)", app.StatusText);

        app.Automatic.ResumeAutomatic();
        Assert.Equal((OverrideMode.Automatic, -1), (app.Session.Switching.OverrideMode, app.Session.Switching.OverrideProfileIndex));
        Assert.Equal(["notepad.exe", "notepad.exe", "Windows", "Windows"], app.AppliedProfiles());
        Assert.Equal("Windows", app.StatusText);
    }

    [Fact]
    public void ProfileOverrideOfADisabledProfileIsIgnored()
    {
        using var app = new SwitchingFixture();
        app.Session.Settings.RememberOverride = true;

        app.Automatic.ToggleProfileOverride(1);

        Assert.Equal(OverrideMode.Automatic, app.Session.Switching.OverrideMode);
        Assert.Empty(app.Backend.Applied);
        Assert.False(File.Exists(app.Session.ConfigPath));
    }

    [Fact]
    public void OverridesAreSavedOnlyWhenRemembered()
    {
        using var forgotten = new SwitchingFixture();
        forgotten.Automatic.ToggleWindowsOverride();
        Assert.False(File.Exists(forgotten.Session.ConfigPath));

        using var remembered = new SwitchingFixture();
        remembered.Session.Settings.RememberOverride = true;
        remembered.Automatic.ToggleWindowsOverride();
        Assert.Contains("\"OverrideMode\": \"Windows\"", File.ReadAllText(remembered.Session.ConfigPath));
    }

    [Fact]
    public void StopAndDisposeReachTheCreatedMonitor()
    {
        using var app = new SwitchingFixture();
        app.Automatic.Start();

        app.Automatic.Stop();
        app.Automatic.Dispose();

        Assert.Equal((1, 1), (app.Monitor.StopCount, app.Monitor.DisposeCount));
    }

    // After an import, the active profile is cleared and the imported global state applied.
    [Fact]
    public void AnImportedPausedStateForcesWindows()
    {
        using var app = new SwitchingFixture { Foreground = "game.exe" };
        app.Automatic.Start();
        app.Monitor.RaiseChanged();
        app.Session.Switching.GlobalPaused = true;
        var names = new List<string>();
        app.Session.Switching.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SwitchingState.ActiveProfileName)) names.Add(app.Session.Switching.ActiveProfileName);
        };

        app.Automatic.ApplyImportedState();

        Assert.Equal([""], names);
        Assert.False(app.Monitor.Running);
        Assert.Equal(["Game", "Game", "Windows", "Windows"], app.AppliedProfiles());
        Assert.Equal("Windows", app.StatusText);
    }

    [Fact]
    public void AnImportedActiveStateStartsTheMonitorAndChecks()
    {
        using var app = new SwitchingFixture { Foreground = "game.exe" };
        app.Session.Switching.GlobalPaused = true;
        app.Automatic.Start();
        app.Session.Switching.GlobalPaused = false;
        var names = new List<string>();
        app.Session.Switching.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SwitchingState.ActiveProfileName)) names.Add(app.Session.Switching.ActiveProfileName);
        };

        app.Automatic.ApplyImportedState();

        Assert.True(app.Monitor.Running);
        Assert.Equal(["", "Game"], names);
        Assert.Equal(["Game", "Game"], app.AppliedProfiles());
    }

    // The same profile as before the import is applied again, because the name was cleared.
    [Fact]
    public void AnImportedStateReappliesTheSameActiveProfile()
    {
        using var app = new SwitchingFixture { Foreground = "game.exe" };
        app.Automatic.Start();
        app.Monitor.RaiseChanged();

        app.Automatic.ApplyImportedState();

        Assert.Equal(["Game", "Game", "Game", "Game"], app.AppliedProfiles());
        Assert.True(app.Monitor.Running);
    }

    [Fact]
    public void AnImportedStateDoesNotTouchAMonitorNotCreatedYet()
    {
        using var app = new SwitchingFixture();

        app.Automatic.ApplyImportedState();
        app.Session.Switching.GlobalPaused = true;
        app.Automatic.ApplyImportedState();

        Assert.Equal((0, 0), (app.Monitor.StartCount, app.Monitor.StopCount));
    }
}
