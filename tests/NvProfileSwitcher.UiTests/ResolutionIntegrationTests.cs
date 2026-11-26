using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NvProfileSwitcher.Core.Tests;
using NvProfileSwitcher.Views;
using static NvProfileSwitcher.Core.Tests.FakeDisplayBackend;

namespace NvProfileSwitcher.UiTests;

// Resolutions over the composed application: the real display refresh after a change, a refresh
// that fails, closing, and the Resolution selector of the editor. The Dell is NVIDIA display 1
// (2560×1440, also 1920×1080 and 1280×960), the LG display 2 (1920×1080). Each resolution change
// gives the displays new NVIDIA handles, as the driver may.
public sealed class ResolutionIntegrationTests
{
    private const uint WmDisplayChange = 0x007E;
    private const string Dell = "DEL4321-ABC123";
    private const string Lg = "GSM5B7F-305NTAB1C2D3";
    private static readonly DisplayResolution DellDesktop = new(2560, 1440);
    private static readonly DisplayResolution Dell1080 = new(1920, 1080);

    private static TestComposition Create(DisplayResolution? gameResolution = null, bool mainWindow = false)
    {
        var app = mainWindow
            ? new TestComposition(createWindow: viewModel => new MainWindow { Width = 1180, Height = 720, DataContext = viewModel })
            : new TestComposition();
        app.Factory.Configure = backend =>
        {
            backend.Displays.Add(Display(Dell, "DELL U2720Q"));
            backend.Displays.Add(Display(Lg, "LG ULTRAGEAR", 2));
        };
        app.Factory.Modes.Add(1, DellDesktop, Dell1080, new DisplayResolution(1280, 960));
        app.Factory.Modes.Add(2, new DisplayResolution(1920, 1080));
        app.Factory.Modes.AfterChange = () =>
        {
            var displays = app.Factory.Created[^1].Displays;
            for (var i = 0; i < displays.Count; i++)
                displays[i] = displays[i] with { Handle = displays[i].Handle + 100 };
        };
        var windows = app.Profiles.Windows!;
        windows.DisplayProfiles.Add(Values(Dell, 50));
        windows.DisplayProfiles.Add(Values(Lg, 55));
        var game = new ProfileListItem("Game") { Enabled = true };
        game.SetExecutables(["game.exe"]);
        game.DisplayProfiles.Add(Values(Dell, 80, gameResolution));
        game.DisplayProfiles.Add(Values(Lg, 60));
        app.Profiles.Load([windows, game]);
        return app;
    }

    private static ProfileDisplaySettings Values(string monitorId, int vibrance, DisplayResolution? resolution = null) => new()
    {
        MonitorId = monitorId,
        DisplayName = monitorId == Dell ? "DELL U2720Q" : "LG ULTRAGEAR",
        DigitalVibrance = vibrance,
        Resolution = resolution,
    };

    private static void Focus(TestComposition app, string executable)
    {
        app.Foreground = executable;
        app.Monitor.RaiseChanged();
        Dispatcher.UIThread.RunJobs();
    }

    private static List<string> DellColors(TestComposition app) =>
        app.Factory.Modes.Timeline.Where(entry => entry.StartsWith($"colors {Dell}", StringComparison.Ordinal)).ToList();

    private static int LastDellVibrance(TestComposition app) =>
        app.Factory.Created.SelectMany(backend => backend.Applied).Last(a => a.MonitorId == Dell).Vibrance;

    [AvaloniaFact]
    public void TheRefreshAfterTheChangeWritesTheColorsWithTheNewHandles()
    {
        using var app = Create(Dell1080);
        app.Window.Show();
        var before = DellColors(app).Count;

        Focus(app, "game.exe");
        Assert.Equal(Dell1080, app.Factory.Modes.Current[1]);
        Assert.Equal(before, DellColors(app).Count);

        // The refresh the change requested, settled with its WM_DISPLAYCHANGE.
        app.Host.DispatchMessage(WmDisplayChange, 0);
        Assert.True(TestComposition.RunUntil(() => DellColors(app).Count > before, TimeSpan.FromSeconds(3)));
        Assert.Equal($"colors {Dell} handle 101", DellColors(app)[before]);
        Assert.Equal(80, LastDellVibrance(app));
        app.Window.Close();
    }

    // A refresh that fails before enumerating writes nothing: the held display is never written
    // with the handles read before its resolution changed. The next refresh writes it.
    [AvaloniaFact]
    public void ARefreshThatFailsWritesNothingWithTheOldHandles()
    {
        using var app = Create(Dell1080);
        app.Window.Show();
        var before = DellColors(app).Count;
        var backend = app.Factory.Created[^1];
        backend.EnumerationFailure = new InvalidOperationException("enumeration failed");

        Focus(app, "game.exe");
        app.Host.DispatchMessage(WmDisplayChange, 0);
        var enumerations = backend.EnumerationCount;
        Assert.True(TestComposition.RunUntil(() => backend.EnumerationCount > enumerations, TimeSpan.FromSeconds(3)));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(before, DellColors(app).Count);

        backend.EnumerationFailure = null;
        app.Host.DispatchMessage(WmDisplayChange, 0);
        Assert.True(TestComposition.RunUntil(() => DellColors(app).Count > before, TimeSpan.FromSeconds(3)));
        Assert.Equal($"colors {Dell} handle 101", DellColors(app)[before]);
        app.Window.Close();
    }

    [AvaloniaFact]
    public void ClosingRestoresTheDesktopResolutionAndTheWindowsColors()
    {
        using var app = Create(Dell1080);
        app.Window.Show();
        Focus(app, "game.exe");

        app.Window.Close();

        Assert.Equal(DellDesktop, app.Factory.Modes.Current[1]);
        Assert.Equal(50, LastDellVibrance(app));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(app.ConfigPath)!, "display-modes.json")));
    }

    // ---- The Resolution selector ----

    private static ComboBox ResolutionCombo(Window window) =>
        window.GetVisualDescendants().OfType<ProfileSettingsView>().Single()
            .GetVisualDescendants().OfType<ComboBox>().Single(combo => combo.Classes.Contains("resolutionCombo"));

    private static void SelectRow(Window window, int index)
    {
        window.GetVisualDescendants().OfType<ProfilesView>().Single().GetVisualDescendants().OfType<ListBox>().Single().SelectedIndex = index;
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void TheSelectorIsShownForApplicationProfilesWithTheResolutionsOffered()
    {
        using var app = Create(mainWindow: true);
        app.ShowActive();

        Assert.False(ResolutionCombo(app.Window).IsVisible);

        SelectRow(app.Window, 1);
        Assert.True(ResolutionCombo(app.Window).IsVisible);
        Assert.Equal(["Don't change", "2560 × 1440", "1920 × 1080", "1280 × 960"], app.ProfileSettings.ResolutionItems.Select(item => item.Text));
        Assert.Equal("Don't change", app.ProfileSettings.SelectedResolutionItem!.Text);
        app.Window.Close();
    }

    [AvaloniaFact]
    public async Task AChosenResolutionIsSavedWithTheProfileWithoutChangingTheColors()
    {
        using var app = Create(mainWindow: true);
        app.ShowActive();
        SelectRow(app.Window, 1);

        app.ProfileSettings.SelectedResolutionItem = app.ProfileSettings.ResolutionItems.Single(item => item.Text == "1920 × 1080");
        Assert.True(app.ProfileSettings.CanCancel);
        Assert.False(app.Editor.HasUnsavedColorChanges);
        await app.ProfileSettings.SaveCommand.ExecuteAsync(null);

        var game = app.Profiles.Profiles.Single(profile => profile.Name == "Game");
        Assert.Equal(Dell1080, game.DisplayProfiles.Single(values => values.MonitorId == Dell).Resolution);
        Assert.Contains("\"Resolution\"", File.ReadAllText(app.ConfigPath));
        // Saving does not apply it: the profile is not the active one.
        Assert.Empty(app.Factory.Modes.Changes);
        app.Window.Close();
    }

    [AvaloniaFact]
    public void ASavedResolutionTheDisplayDoesNotOfferIsKeptAndMarked()
    {
        using var app = Create(new DisplayResolution(800, 600), mainWindow: true);
        app.ShowActive();
        SelectRow(app.Window, 1);

        Assert.Equal("800 × 600 · Not supported", app.ProfileSettings.SelectedResolutionItem!.Text);
        Assert.False(app.ProfileSettings.CanCancel);
        app.Window.Close();
    }
}
