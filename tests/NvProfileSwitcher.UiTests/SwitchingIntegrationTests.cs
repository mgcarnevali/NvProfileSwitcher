using Avalonia.Headless.XUnit;
using NvProfileSwitcher.Core;
using NvProfileSwitcher.Core.Tests;
using NvProfileSwitcher.Models;
using static NvProfileSwitcher.Core.Tests.FakeDisplayBackend;

namespace NvProfileSwitcher.UiTests;

// Color application and automatic switching across the window lifecycle: opening, showing
// the window again, display topology changes and closing.
public sealed class SwitchingIntegrationTests
{
    private const uint WmDisplayChange = 0x007E;
    private const string Dell = "DEL4321-ABC123";
    private const string Lg = "GSM5B7F-305NTAB1C2D3";

    // Two displays; the Windows profile and an application profile with distinct values.
    private static TestComposition Create()
    {
        var app = new TestComposition();
        app.Factory.Configure = backend =>
        {
            backend.Displays.Add(Display(Dell, "DELL U2720Q"));
            backend.Displays.Add(Display(Lg, "LG ULTRAGEAR", 2));
        };
        var windows = app.Profiles.Windows!;
        windows.DisplayProfiles.Add(Values(Dell, 50));
        windows.DisplayProfiles.Add(Values(Lg, 55));
        var game = new ProfileListItem("Game") { Enabled = true };
        game.SetExecutables(["game.exe"]);
        game.DisplayProfiles.Add(Values(Dell, 80));
        game.DisplayProfiles.Add(Values(Lg, 60));
        app.Profiles.Load([windows, game]);
        return app;
    }

    private static ProfileDisplaySettings Values(string monitorId, int vibrance) => new()
    {
        MonitorId = monitorId,
        DisplayName = monitorId == Dell ? "DELL U2720Q" : "LG ULTRAGEAR",
        DigitalVibrance = vibrance,
    };

    // The profile of each application, in order, identified by its vibrance.
    private static List<string> Applied(TestComposition app) =>
        app.Factory.Created.SelectMany(backend => backend.Applied)
            .Select(a => a.Vibrance switch { 50 or 55 => "Windows", 80 or 60 => "Game", _ => "?" })
            .ToList();

    private static string StatusText(TestComposition app) =>
        ActiveProfileCore.StatusText(app.Switching.DisplayedActiveProfile);

    private static void ShowAgain(TestComposition app)
    {
        app.Window.Hide();
        app.Window.Show();
    }

    [AvaloniaFact]
    public void FirstOpeningRestoresWindowsAndStartsSwitching()
    {
        using var app = Create();

        app.Window.Show();

        Assert.Equal(["Windows", "Windows"], Applied(app));
        Assert.Equal(1, app.Monitor.StartCount);
        Assert.Equal("Windows", StatusText(app));
    }

    [AvaloniaFact]
    public void FirstOpeningWhilePausedForcesWindowsWithoutStartingSwitching()
    {
        using var app = Create();
        app.Switching.GlobalPaused = true;

        app.Window.Show();

        Assert.Equal(["Windows", "Windows"], Applied(app));
        Assert.Equal(0, app.Monitor.StartCount);
        Assert.Equal("Windows", StatusText(app));
    }

    // Until the API is initialized, the status bar keeps the original initial text even when
    // the configuration paused global switching.
    [AvaloniaFact]
    public void ActiveProfileIsPublishedOnlyAfterTheApiIsInitialized()
    {
        using var app = Create();
        app.Switching.GlobalPaused = true;
        app.Factory.NextFailure = new DllNotFoundException("nvapi64.dll");

        app.Window.Show();

        Assert.Equal("Windows", StatusText(app));
        Assert.Empty(Applied(app));
    }

    [AvaloniaFact]
    public void ForegroundChangesSwitchTheActiveProfile()
    {
        using var app = Create();
        app.Window.Show();
        app.Foreground = "game.exe";

        app.Monitor.RaiseChanged();

        Assert.Equal(["Windows", "Windows", "Game", "Game"], Applied(app));
        Assert.Equal("Game", StatusText(app));
    }

    // Option C: showing the window again with switching active applies the active profile
    // once, through the display refresh, and does not restore Windows on top of it.
    [AvaloniaFact]
    public void ShowingAgainKeepsTheProfileOverrideApplied()
    {
        using var app = Create();
        app.Window.Show();
        app.AutomaticSwitching.ToggleProfileOverride(0);
        var before = Applied(app).Count;

        ShowAgain(app);

        Assert.Equal(["Game", "Game"], Applied(app).Skip(before));
        Assert.Equal("Game", app.Switching.ActiveProfileName);
        Assert.Equal("Game (override)", StatusText(app));
        Assert.Equal(1, app.Monitor.StartCount);
    }

    [AvaloniaFact]
    public void ShowingAgainAppliesTheForegroundProfileOnce()
    {
        using var app = Create();
        app.Window.Show();
        app.Foreground = "game.exe";
        var before = Applied(app).Count;

        ShowAgain(app);

        Assert.Equal(["Game", "Game"], Applied(app).Skip(before));
        Assert.Equal("Game", StatusText(app));
    }

    [AvaloniaFact]
    public void ShowingAgainWhilePausedForcesWindowsWithoutChecking()
    {
        using var app = Create();
        app.Window.Show();
        app.AutomaticSwitching.SetGlobalEnabled(false);
        app.Foreground = "game.exe";
        var before = Applied(app).Count;

        ShowAgain(app);

        Assert.Equal(["Windows", "Windows"], Applied(app).Skip(before));
        Assert.Equal("Windows", StatusText(app));
    }

    // A failed enumeration skips the reevaluation; the colors and the active profile stay
    // as they were, so they remain in sync.
    [AvaloniaFact]
    public void ShowingAgainWithAFailedEnumerationKeepsTheActiveProfile()
    {
        using var app = Create();
        app.Window.Show();
        app.Foreground = "game.exe";
        app.Monitor.RaiseChanged();
        app.Factory.Configure += backend => backend.EnumerationFailure = new InvalidOperationException("enumeration failed");
        var before = Applied(app).Count;

        ShowAgain(app);

        Assert.Empty(Applied(app).Skip(before));
        Assert.Equal("Game", app.Switching.ActiveProfileName);
    }

    // An exception while restoring Windows at opening reports the API as unavailable, as in
    // the original, and leaves the colors partially applied.
    [AvaloniaFact]
    public void FailedApplicationAtOpeningReportsTheApiUnavailable()
    {
        using var app = Create();
        app.Factory.Configure += backend =>
            backend.ApplyFailure = display => display.MonitorId == Lg ? new InvalidOperationException("NVAPI call failed") : null;

        app.Window.Show();

        Assert.False(app.Nvidia.IsAvailable);
        Assert.Equal(["Windows"], Applied(app));
        Assert.Equal(0, app.Monitor.StartCount);

        // Closing restores Windows and would propagate the same exception, as in the original.
        app.Factory.Created[0].ApplyFailure = null;
    }

    [AvaloniaFact]
    public void TopologyChangesReevaluateTheActiveProfile()
    {
        using var app = Create();
        app.Window.Show();
        app.Foreground = "game.exe";
        var before = Applied(app).Count;

        app.Host.DispatchMessage(WmDisplayChange, 0);
        Assert.True(TestComposition.RunUntil(() => Applied(app).Count > before, TimeSpan.FromSeconds(3)));

        Assert.Equal(["Game", "Game"], Applied(app).Skip(before));
        Assert.Equal("Game", StatusText(app));
    }

    [AvaloniaFact]
    public void TopologyChangesWhilePausedDoNotReevaluate()
    {
        using var app = Create();
        app.Window.Show();
        app.AutomaticSwitching.SetGlobalEnabled(false);
        app.Foreground = "game.exe";
        var backend = app.Factory.Created[0];
        var before = Applied(app).Count;

        app.Host.DispatchMessage(WmDisplayChange, 0);
        Assert.True(TestComposition.RunUntil(() => backend.EnumerationCount == 2, TimeSpan.FromSeconds(3)));

        Assert.Equal(before, Applied(app).Count);
    }

    [AvaloniaFact]
    public void ClosingStopsSwitchingAndRestoresWindows()
    {
        using var app = Create();
        app.Window.Show();
        app.Foreground = "game.exe";
        app.Monitor.RaiseChanged();

        app.Window.Close();

        Assert.Equal(["Windows", "Windows", "Game", "Game", "Windows", "Windows"], Applied(app));
        Assert.Equal(1, app.Monitor.StopCount);
    }

    // As in the original, closing while paused does not force Windows.
    [AvaloniaFact]
    public void ClosingWhilePausedDoesNotApply()
    {
        using var app = Create();
        app.Window.Show();
        app.AutomaticSwitching.SetGlobalEnabled(false);
        var before = Applied(app).Count;

        app.Window.Close();

        Assert.Equal(before, Applied(app).Count);
    }

    [AvaloniaFact]
    public void ClosedDisposesTheMonitorBeforeReleasingTheApi()
    {
        using var app = Create();
        app.Window.Show();

        app.Window.Close();

        Assert.Equal(["create 1", "monitor dispose", "dispose 1"], app.Factory.Log);
    }
}
