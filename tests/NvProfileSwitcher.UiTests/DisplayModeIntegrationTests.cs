using Avalonia.Headless.XUnit;
using NvProfileSwitcher.Core;
using NvProfileSwitcher.Core.Tests;
using NvProfileSwitcher.Services;
using static NvProfileSwitcher.Core.Tests.FakeDisplayBackend;

namespace NvProfileSwitcher.UiTests;

// Display modes over the composed application: the startup recovery, the display refresh after a
// change, closing, and a configuration without resolutions. One display, \\.\DISPLAY1, at
// 2560×1440 and 144 Hz; the Game profile gives it 1920×1080 when the test says so.
public sealed class DisplayModeIntegrationTests
{
    private const uint WmDisplayChange = 0x007E;
    private const string Dell = "DEL4321-ABC123";
    private const string DellGdi = @"\\.\DISPLAY1";

    private static readonly DisplayModeInfo Desktop = new(2560, 1440, 144);
    private static readonly DisplayModeInfo Game1080 = new(1920, 1080, 144);

    private static string Config(bool withResolution) => $$"""
        {"Application Profiles":[
          {"Name":"Game","Exe Paths":["game.exe"],"Enabled":true,"Hotkey":0,
           "Display Profiles":[{"DisplayName":"DELL","MonitorId":"{{Dell}}","Brightness":100,"Contrast":100,"Gamma":1,"DigitalVibrance":80,"Hue":0{{(withResolution ? ",\"Resolution\":{\"Width\":1920,\"Height\":1080}" : "")}}}]}],
         "Windows Profiles":[{"DisplayName":"DELL","MonitorId":"{{Dell}}","Brightness":100,"Contrast":100,"Gamma":1,"DigitalVibrance":50,"Hue":0}]}
        """;

    private static TestComposition Create(bool withResolution = true)
    {
        var app = new TestComposition(Config(withResolution));
        app.DisplayModes.AddMonitor(DellGdi, Desktop, Game1080, new DisplayModeInfo(1280, 960, 120));
        app.Factory.Configure = backend => backend.Displays.Add(Display(Dell, "DELL U2720Q"));
        return app;
    }

    private static string RecoveryPath(TestComposition app) =>
        Path.Combine(Path.GetDirectoryName(app.ConfigPath)!, "display-modes.json");

    private static FakeDisplayBackend Backend(TestComposition app) => app.Factory.Created[^1];

    private static void Focus(TestComposition app, string executable)
    {
        app.Foreground = executable;
        app.Monitor.RaiseChanged();
    }

    [AvaloniaFact]
    public void TheFirstOpeningRestoresWhatAPreviousSessionLeftChanged()
    {
        using var app = Create();
        app.DisplayModes.Current[DellGdi] = Game1080;
        new DisplayModeRecoveryStore(RecoveryPath(app)).Save([new DisplayModeRecoveryEntry(Dell, Desktop, Game1080, Game1080)]);

        app.Window.Show();

        Assert.Equal(Desktop, app.DisplayModes.Current[DellGdi]);
        Assert.False(File.Exists(RecoveryPath(app)));
    }

    // The change requests a display refresh by itself: the displays are enumerated again and the
    // colors written again, and that refresh changes no mode.
    [AvaloniaFact]
    public void AChangeRefreshesTheDisplaysOnceWithoutChangingAgain()
    {
        using var app = Create();
        app.Window.Show();
        var backend = Backend(app);
        var enumerations = backend.EnumerationCount;

        Focus(app, "game.exe");
        Assert.Equal(Game1080, app.DisplayModes.Current[DellGdi]);
        var written = backend.Applied.Count;

        Assert.True(TestComposition.RunUntil(() => backend.EnumerationCount == enumerations + 1, TimeSpan.FromSeconds(3)));
        Assert.True(backend.Applied.Count > written);
        TestComposition.RunUntil(() => false, TimeSpan.FromMilliseconds(800));
        Assert.Equal(enumerations + 1, backend.EnumerationCount);
        Assert.Single(app.DisplayModes.Applied);
        Assert.Equal("Game", app.Switching.ActiveProfileName);
    }

    // A monitor that returns gets the resolution in the refresh, before its colors are written.
    [AvaloniaFact]
    public void AReturningMonitorGetsItsResolutionBeforeItsColors()
    {
        using var app = Create();
        app.Window.Show();
        Focus(app, "game.exe");
        var backend = Backend(app);
        var removed = backend.Displays.Single();

        backend.Displays.Clear();
        app.Host.DispatchMessage(WmDisplayChange, 0);
        Assert.True(TestComposition.RunUntil(() => app.Nvidia.Displays.Count == 0, TimeSpan.FromSeconds(3)));
        app.DisplayModes.Current[DellGdi] = Desktop;
        backend.Displays.Add(removed);
        var writtenBeforeTheMode = -1;
        app.DisplayModes.DuringApply = () => writtenBeforeTheMode = backend.Applied.Count;
        app.Host.DispatchMessage(WmDisplayChange, 0);
        Assert.True(TestComposition.RunUntil(() => app.Nvidia.Displays.Count == 1, TimeSpan.FromSeconds(3)));

        Assert.Equal(Game1080, app.DisplayModes.Current[DellGdi]);
        Assert.True(writtenBeforeTheMode >= 0);
        Assert.True(backend.Applied.Count > writtenBeforeTheMode);
        Assert.Equal(80, backend.Applied[^1].Vibrance);
    }

    [AvaloniaFact]
    public void ClosingRestoresTheDesktopModeBeforeTheColors()
    {
        using var app = Create();
        app.Window.Show();
        Focus(app, "game.exe");
        var backend = Backend(app);
        var writtenBeforeTheMode = -1;
        app.DisplayModes.DuringApply = () => writtenBeforeTheMode = backend.Applied.Count;

        app.Window.Close();
        Assert.True(TestComposition.RunUntil(() => !app.Window.IsVisible, TimeSpan.FromSeconds(5)));

        Assert.Equal(Desktop, app.DisplayModes.Current[DellGdi]);
        Assert.False(File.Exists(RecoveryPath(app)));
        Assert.True(backend.Applied.Count > writtenBeforeTheMode);
        Assert.Equal(50, backend.Applied[^1].Vibrance);
    }

    // A failure of the display modes never stops the colors.
    [AvaloniaFact]
    public void AnUnexpectedModeFailureLeavesTheColorsApplied()
    {
        using var app = Create();
        app.DisplayModes.Failure = _ => new InvalidOperationException("driver");
        app.Window.Show();

        Focus(app, "game.exe");
        Assert.Equal(80, Backend(app).Applied[^1].Vibrance);

        app.Window.Close();
        Assert.True(TestComposition.RunUntil(() => !app.Window.IsVisible, TimeSpan.FromSeconds(5)));
        Assert.Equal(50, Backend(app).Applied[^1].Vibrance);
    }

    // Users without resolutions: nothing reaches the display mode backend, from the first opening
    // to closing, through switching, overrides, Global Switching and display changes.
    [AvaloniaFact]
    public void WithoutResolutionsTheBackendIsNeverReached()
    {
        using var app = Create(withResolution: false);
        app.Window.Show();
        var backend = Backend(app);

        Focus(app, "game.exe");
        Focus(app, "explorer.exe");
        app.AutomaticSwitching.ToggleWindowsOverride();
        app.AutomaticSwitching.ResumeAutomatic();
        app.AutomaticSwitching.SetGlobalEnabled(false);
        app.AutomaticSwitching.SetGlobalEnabled(true);
        var enumerations = backend.EnumerationCount;
        app.Host.DispatchMessage(WmDisplayChange, 0);
        Assert.True(TestComposition.RunUntil(() => backend.EnumerationCount > enumerations, TimeSpan.FromSeconds(3)));
        app.Window.Close();
        Assert.True(TestComposition.RunUntil(() => !app.Window.IsVisible, TimeSpan.FromSeconds(5)));

        Assert.Empty(app.DisplayModes.Calls);
        Assert.False(File.Exists(RecoveryPath(app)));
        Assert.Contains(backend.Applied, application => application.Vibrance == 80);
    }
}
