using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NvProfileSwitcher.Core.Tests;
using NvProfileSwitcher.Services;
using static NvProfileSwitcher.Core.Tests.FakeDisplayBackend;

namespace NvProfileSwitcher.UiTests;

// NVIDIA API initialization, display refresh, topology changes and their window lifecycle.
public sealed class DisplayIntegrationTests
{
    private const uint WmDisplayChange = 0x007E;
    private const string Dell = "DEL4321-ABC123";
    private const string Lg = "GSM5B7F-305NTAB1C2D3";

    private static TestComposition WithOneDisplay()
    {
        var app = new TestComposition();
        app.Factory.Configure = backend => backend.Displays.Add(Display(Dell, "DELL U2720Q"));
        return app;
    }

    [AvaloniaFact]
    public void OpenedInitializesTheApiAndSynchronizesTheDisplays()
    {
        using var app = WithOneDisplay();

        app.Window.Show();

        Assert.True(app.Nvidia.IsAvailable);
        Assert.Equal("581.57", app.Nvidia.DriverVersion);
        Assert.Equal(Dell, Assert.Single(app.Profiles.Windows!.DisplayProfiles).MonitorId);
        Assert.True(File.Exists(app.ConfigPath));
    }

    [AvaloniaFact]
    public void FailedInitializationReportsUnavailableWithoutRefreshing()
    {
        using var app = WithOneDisplay();
        app.Factory.NextFailure = new DllNotFoundException("nvapi64.dll");

        app.Window.Show();

        Assert.False(app.Nvidia.IsAvailable);
        Assert.Equal("Unknown", app.Nvidia.DriverVersion);
        Assert.Empty(app.Profiles.Windows!.DisplayProfiles);
        Assert.False(File.Exists(app.ConfigPath));
    }

    // Showing the window again from the tray raises Opened again: the API is initialized
    // again, the displays are refreshed again and exactly one instance remains alive.
    [AvaloniaFact]
    public void ShowingTheWindowAgainInitializesTheApiAgainWithOneLiveInstance()
    {
        using var app = WithOneDisplay();

        app.Window.Show();
        app.Window.Hide();
        app.Window.Show();

        Assert.Equal(["create 1", "create 2", "dispose 1"], app.Factory.Log);
        Assert.Equal(1, app.Factory.Created[1].EnumerationCount);
    }

    [AvaloniaFact]
    public void FailedInitializationRetriesWhenTheWindowIsShownAgain()
    {
        using var app = WithOneDisplay();
        app.Factory.NextFailure = new DllNotFoundException("nvapi64.dll");
        app.Window.Show();
        app.Window.Hide();

        app.Window.Show();

        Assert.True(app.Nvidia.IsAvailable);
        Assert.Single(app.Profiles.Windows!.DisplayProfiles);
    }

    [AvaloniaFact]
    public void FailedReinitializationKeepsTheCurrentInstanceAndReportsUnavailable()
    {
        using var app = WithOneDisplay();
        app.Window.Show();
        app.Window.Hide();
        app.Factory.NextFailure = new InvalidOperationException("NvAPI_Initialize failed: -1");

        app.Window.Show();

        Assert.False(app.Nvidia.IsAvailable);
        Assert.Equal("Unknown", app.Nvidia.DriverVersion);
        Assert.True(app.Nvidia.IsInitialized);
        Assert.Equal(0, app.Factory.Created[0].DisposeCount);
    }

    [AvaloniaFact]
    public void FailedDisplayRefreshIsIgnored()
    {
        using var app = new TestComposition();
        app.Factory.Configure = backend => backend.EnumerationFailure = new InvalidOperationException("enumeration failed");

        app.Window.Show();

        Assert.True(app.Nvidia.IsAvailable);
        Assert.Equal("581.57", app.Nvidia.DriverVersion);
        Assert.False(File.Exists(app.ConfigPath));
    }

    [AvaloniaFact]
    public void TopologyChangesAreDebouncedIntoOneRefresh()
    {
        using var app = WithOneDisplay();
        app.Window.Show();
        var backend = app.Factory.Created[0];
        backend.Displays.Add(Display(Lg, "LG ULTRAGEAR", 2));
        var watch = Stopwatch.StartNew();

        Assert.False(app.Host.DispatchMessage(WmDisplayChange, 0));
        Assert.False(app.Host.DispatchMessage(WmDisplayChange, 0));
        Assert.False(app.Host.DispatchMessage(WmDisplayChange, 0));

        Assert.True(TestComposition.RunUntil(() => backend.EnumerationCount == 2, TimeSpan.FromSeconds(3)));
        Assert.True(watch.ElapsedMilliseconds >= 450, $"refreshed after {watch.ElapsedMilliseconds} ms");
        Assert.Equal(2, app.Profiles.Windows!.DisplayProfiles.Count);

        TestComposition.RunUntil(() => false, TimeSpan.FromMilliseconds(700));
        Assert.Equal(2, backend.EnumerationCount);
    }

    // A monitor whose identity cannot be read yet is left out, and the refresh runs once more,
    // without a new message, when Windows has had time to report it.
    [AvaloniaFact]
    public void ADisplayWithoutIdentityIsRefreshedOnceMore()
    {
        using var app = WithOneDisplay();
        app.Window.Show();
        var backend = app.Factory.Created[0];
        backend.Displays.Add(Display("", "LG ULTRAGEAR", 2));

        app.Host.DispatchMessage(WmDisplayChange, 0);
        Assert.True(TestComposition.RunUntil(() => backend.EnumerationCount == 2, TimeSpan.FromSeconds(3)));
        Assert.Equal([Dell], app.Nvidia.Displays.Select(d => d.MonitorId));

        backend.Displays[1] = Display(Lg, "LG ULTRAGEAR", 2);
        Assert.True(TestComposition.RunUntil(() => backend.EnumerationCount == 3, DisplayTopologyService.RetryDelay * 2));
        Assert.Equal([Dell, Lg], app.Nvidia.Displays.Select(d => d.MonitorId));
        Assert.NotNull(DisplayCore.FindValues(app.Profiles.Windows!, Lg));
    }

    // The refresh runs once more per topology change: a monitor without readable identity does
    // not keep the application refreshing.
    [AvaloniaFact]
    public void ADisplayThatNeverHasAnIdentityIsRefreshedOnlyOnceMore()
    {
        using var app = WithOneDisplay();
        app.Window.Show();
        var backend = app.Factory.Created[0];
        backend.Displays.Add(Display("", "Projector", 2));

        app.Host.DispatchMessage(WmDisplayChange, 0);
        Assert.True(TestComposition.RunUntil(() => backend.EnumerationCount == 3, DisplayTopologyService.RetryDelay * 2));
        TestComposition.RunUntil(() => false, DisplayTopologyService.RetryDelay + TimeSpan.FromMilliseconds(500));

        Assert.Equal(3, backend.EnumerationCount);
        Assert.DoesNotContain(backend.Applied, a => string.IsNullOrWhiteSpace(a.MonitorId));
    }

    // A driver installed while the application runs is shown when the window is active again,
    // although the API library loaded at start keeps reporting the previous one.
    [AvaloniaFact]
    public void ADriverInstalledWhileRunningIsShownWhenTheWindowIsActivatedAgain()
    {
        using var app = WithOneDisplay();
        app.Factory.Configure += backend => backend.DriverVersion = "616.64";
        app.InstalledDriverVersion = "616.64";
        app.ShowActive();
        Assert.Equal("616.64", app.ViewModel.StatusBar.DriverText);

        app.InstalledDriverVersion = "617.42";
        app.Deactivate();
        Assert.Equal("616.64", app.ViewModel.StatusBar.DriverText);
        app.Window.Activate();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("617.42", app.ViewModel.StatusBar.DriverText);
        Assert.Single(app.Factory.Created);
    }

    // Installing a driver restarts the display adapter: the display change shows the installed
    // driver without the window being activated.
    [AvaloniaFact]
    public void ADriverInstalledWhileRunningIsShownAfterTheDisplayChange()
    {
        using var app = WithOneDisplay();
        app.Factory.Configure += backend => backend.DriverVersion = "616.64";
        app.InstalledDriverVersion = "616.64";
        app.ShowActive();
        app.Deactivate();
        var backend = app.Factory.Created[0];

        app.InstalledDriverVersion = "617.42";
        app.Host.DispatchMessage(WmDisplayChange, 0);
        Assert.True(TestComposition.RunUntil(() => backend.EnumerationCount == 2, TimeSpan.FromSeconds(3)));

        Assert.Equal("617.42", app.ViewModel.StatusBar.DriverText);
        Assert.False(app.Window.IsActive);
        Assert.Single(app.Factory.Created);
    }

    [AvaloniaFact]
    public void TopologyServiceReactsOnlyToDisplayChanges()
    {
        var topology = new DisplayTopologyService();
        var changes = 0;
        topology.TopologyChanged += () => changes++;

        Assert.False(topology.HandleMessage(0x0312, 3001));
        TestComposition.RunUntil(() => false, TimeSpan.FromMilliseconds(700));

        Assert.Equal(0, changes);
    }

    [AvaloniaFact]
    public void TopologyChangeBeforeTheApiIsInitializedIsIgnored()
    {
        using var app = WithOneDisplay();

        app.Host.DispatchMessage(WmDisplayChange, 0);
        TestComposition.RunUntil(() => false, TimeSpan.FromMilliseconds(700));

        Assert.Empty(app.Factory.Created);
        Assert.False(File.Exists(app.ConfigPath));
    }

    [AvaloniaFact]
    public void ActivationRefreshesTheDriverVersion()
    {
        using var app = WithOneDisplay();
        app.Window.Show();
        app.Factory.Created[0].DriverVersion = "582.10";

        app.Window.Activate();

        Assert.True(TestComposition.RunUntil(() => app.Nvidia.DriverVersion == "582.10", TimeSpan.FromSeconds(1)));
    }

    [AvaloniaFact]
    public void ClosingReleasesTheApiAndStopsPendingTopologyChanges()
    {
        using var app = WithOneDisplay();
        app.Window.Show();
        var backend = app.Factory.Created[0];
        app.Host.DispatchMessage(WmDisplayChange, 0);

        app.Window.Close();
        TestComposition.RunUntil(() => false, TimeSpan.FromMilliseconds(700));

        Assert.Equal(1, backend.DisposeCount);
        Assert.Equal(1, backend.EnumerationCount);
        Assert.False(app.Nvidia.IsInitialized);
    }
}
