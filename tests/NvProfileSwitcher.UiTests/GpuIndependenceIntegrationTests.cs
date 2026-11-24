using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NvProfileSwitcher.Core;
using NvProfileSwitcher.Core.Tests;
using NvProfileSwitcher.Services;
using NvProfileSwitcher.Views;
using static NvProfileSwitcher.Core.Tests.FakeDisplayBackend;

namespace NvProfileSwitcher.UiTests;

// The composed application on a computer without the NVIDIA driver, and when the driver becomes
// available while it runs: everything but the colors works, nothing claims colors were applied,
// and the colors saved in the profiles are kept.
public sealed class GpuIndependenceIntegrationTests
{
    private const uint WmDisplayChange = 0x007E;
    private const uint WmPowerBroadcast = 0x0218;
    private const nint PbtApmResumeAutomatic = 0x0012;
    private const string Dell = "DEL4321-ABC123";
    private const string DellGdi = @"\\.\DISPLAY1";

    private static readonly DisplayModeInfo Desktop = new(2560, 1440, 144);
    private static readonly DisplayModeInfo Game1080 = new(1920, 1080, 144);

    private const string Values = """
        "DisplayName":"DELL","MonitorId":"DEL4321-ABC123","Brightness":100,"Contrast":100,"Gamma":1
        """;

    // Saved colors for the Dell in both profiles, and a resolution for the game.
    private static readonly string SavedColors = $$$"""
        {"Windows Profiles":[{ {{{Values}}},"DigitalVibrance":50,"Hue":0}],
         "Application Profiles":[{"Name":"Game","Exe Paths":["game.exe"],"Enabled":true,"Hotkey":0,
           "Display Profiles":[{ {{{Values}}},"DigitalVibrance":80,"Hue":10,"Resolution":{"Width":1920,"Height":1080}}]}]}
        """;

    // No values for the Dell anywhere but a resolution, held with pending colors.
    private static readonly string PendingColors = $$$"""
        {"Application Profiles":[{"Name":"Game","Exe Paths":["game.exe"],"Enabled":true,"Hotkey":0,
           "Display Profiles":[{ {{{Values}}},"DigitalVibrance":50,"Hue":0,"ColorsPending":true,"Resolution":{"Width":1920,"Height":1080}}]}]}
        """;

    private static TestComposition WithoutNvidia(string config)
    {
        var app = new TestComposition(config, createWindow: viewModel => new MainWindow { Width = 1180, Height = 720, DataContext = viewModel });
        app.Factory.Failure = new DllNotFoundException("nvapi64.dll");
        app.Factory.Configure = backend =>
        {
            backend.Displays.Add(Display(Dell, "DELL U2720Q"));
            backend.Vibrance[Dell] = 70;
            backend.Hue[Dell] = 5;
        };
        app.DisplayModes.AddMonitor(DellGdi, Desktop, Game1080);
        return app;
    }

    private static void Focus(TestComposition app, string executable)
    {
        app.Foreground = executable;
        app.Monitor.RaiseChanged();
    }

    private static JsonNode DellValues(TestComposition app, string section, int profile = 0)
    {
        var root = JsonNode.Parse(File.ReadAllText(app.ConfigPath))!;
        var values = section == "Windows Profiles"
            ? root[section]!.AsArray()
            : root[section]![profile]!["Display Profiles"]!.AsArray();
        return values.Single(v => (string?)v!["MonitorId"] == Dell)!;
    }

    private static T Control<T>(Window window, Func<T, bool> match) where T : Control =>
        window.GetVisualDescendants().OfType<ProfileSettingsView>().Single().GetVisualDescendants().OfType<T>().Single(match);

    [AvaloniaFact]
    public void WithoutTheDriverEverythingButTheColorsWorks()
    {
        using var app = WithoutNvidia(SavedColors);
        app.Window.Show();

        Assert.Equal(NvidiaStatus.NotDetected, app.Nvidia.Status);
        Assert.Equal("Not detected", app.ViewModel.StatusBar.NvApiStatusText);
        Assert.Equal("—", app.ViewModel.StatusBar.DriverText);
        var display = Assert.Single(app.Displays.Displays);
        Assert.False(display.CanAdjustColors);
        Assert.Equal(1, app.Monitor.StartCount);

        Focus(app, "game.exe");
        Assert.Equal("Game", app.Switching.ActiveProfileName);
        Assert.Equal(Game1080, app.DisplayModes.Current[DellGdi]);

        Focus(app, "explorer.exe");
        Assert.Equal(Desktop, app.DisplayModes.Current[DellGdi]);

        app.AutomaticSwitching.ToggleWindowsOverride();
        app.AutomaticSwitching.ResumeAutomatic();
        app.Window.Close();
        Assert.True(TestComposition.RunUntil(() => !app.Window.IsVisible, TimeSpan.FromSeconds(5)));

        // Nothing was written, and the saved colors are as they were.
        Assert.Empty(app.Factory.Created);
        Assert.Equal(Desktop, app.DisplayModes.Current[DellGdi]);
        Assert.Equal(80, (int)DellValues(app, "Application Profiles")["DigitalVibrance"]!);
        Assert.Equal(10, (int)DellValues(app, "Application Profiles")["Hue"]!);
        Assert.Equal(50, (int)DellValues(app, "Windows Profiles")["DigitalVibrance"]!);
    }

    [AvaloniaFact]
    public void TheColorControlsAreDisabledWithANoticeAndTheResolutionStaysEditable()
    {
        using var app = WithoutNvidia(SavedColors);
        app.ShowActive();
        var window = (MainWindow)app.Window;
        window.GetVisualDescendants().OfType<ProfilesView>().Single().GetVisualDescendants().OfType<ListBox>().Single().SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();

        Assert.False(app.ProfileSettings.AreColorsAvailable);
        Assert.False(Control<Grid>(window, g => g.Classes.Contains("colorControls")).IsEnabled);
        Assert.All(window.GetVisualDescendants().OfType<Slider>(), slider => Assert.False(slider.IsEffectivelyEnabled));
        Assert.True(Control<TextBlock>(window, t => t.Classes.Contains("colorsUnavailable")).IsVisible);
        Assert.False(Control<Button>(window, b => (b.Content as string) == "Copy settings").IsEffectivelyEnabled);
        Assert.False(Control<Button>(window, b => (b.Content as string) == "Reset").IsEffectivelyEnabled);
        Assert.True(Control<ComboBox>(window, c => c.Classes.Contains("resolutionCombo")).IsEffectivelyEnabled);
        Assert.Contains("colors unavailable", Assert.Single(app.ProfileSettings.Displays).SelectorLabel);
        window.Close();
    }

    // The driver installed while the application runs: the display change it causes initializes
    // the API, the colors become available, and the pending colors take the ones read from the
    // display instead of being applied.
    [AvaloniaFact]
    public void ADriverInstalledLaterEnablesTheColorsWithoutApplyingMadeUpOnes()
    {
        using var app = WithoutNvidia(PendingColors);
        app.Window.Show();
        Assert.False(app.ProfileSettings.AreColorsAvailable);
        Assert.Empty(JsonNode.Parse(File.ReadAllText(app.ConfigPath))!["Windows Profiles"]?.AsArray() ?? []);

        app.Factory.Failure = null;
        app.Host.DispatchMessage(WmDisplayChange, 0);
        Assert.True(TestComposition.RunUntil(() => app.Displays.Displays is [{ CanAdjustColors: true }], TimeSpan.FromSeconds(3)));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(NvidiaStatus.Available, app.Nvidia.Status);
        Assert.True(app.ProfileSettings.AreColorsAvailable);
        Assert.Equal(70, (int)DellValues(app, "Windows Profiles")["DigitalVibrance"]!);
        var game = DellValues(app, "Application Profiles");
        Assert.Equal((70, 5), ((int)game["DigitalVibrance"]!, (int)game["Hue"]!));
        Assert.Null(game["ColorsPending"]);
        Assert.Equal(1920, (int)game["Resolution"]!["Width"]!);

        Focus(app, "game.exe");
        var written = app.Factory.Created[^1].Current(Dell)!.Value;
        Assert.Equal((70, 5), (written.Vibrance, written.Hue));
        app.Window.Close();
    }

    // A return from sleep refreshes the displays and their resolutions without the API.
    [AvaloniaFact]
    public void AResumeWithoutTheDriverRefreshesTheDisplays()
    {
        using var app = WithoutNvidia(SavedColors);
        app.Window.Show();
        var enumerations = app.Factory.EnumerationCount;

        app.Host.DispatchMessage(WmPowerBroadcast, PbtApmResumeAutomatic);

        Assert.True(TestComposition.RunUntil(() => app.Factory.EnumerationCount > enumerations, TimeSpan.FromSeconds(3)));
        Assert.Equal(NvidiaStatus.NotDetected, app.Nvidia.Status);
        app.Window.Close();
    }
}
