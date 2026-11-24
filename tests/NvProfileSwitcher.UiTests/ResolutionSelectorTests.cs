using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NvProfileSwitcher.Core;
using NvProfileSwitcher.Models;
using NvProfileSwitcher.Views;
using static NvProfileSwitcher.Core.Tests.FakeDisplayBackend;

namespace NvProfileSwitcher.UiTests;

// The Resolution selector of the profile editor over the composed application, with one
// display (\\.\DISPLAY1) at 2560×1440 and 144 Hz.
public sealed class ResolutionSelectorTests
{
    private const string Dell = "DEL4321-ABC123";
    private const string DellGdi = @"\\.\DISPLAY1";

    private static string Config(string resolution = "") => $$"""
        {"Application Profiles":[
          {"Name":"Game","Exe Paths":["game.exe"],"Enabled":true,"Hotkey":0,
           "Display Profiles":[{"DisplayName":"DELL","MonitorId":"{{Dell}}","Brightness":100,"Contrast":100,"Gamma":1,"DigitalVibrance":50,"Hue":0{{resolution}}}]}]}
        """;

    private static (TestComposition App, MainWindow Window) Show(string? config = null)
    {
        var app = new TestComposition(
            config ?? Config(), createWindow: viewModel => new MainWindow { Width = 1180, Height = 720, DataContext = viewModel });
        app.DisplayModes.AddMonitor(DellGdi, new DisplayModeInfo(2560, 1440, 144),
            new DisplayModeInfo(1920, 1080, 144), new DisplayModeInfo(1920, 1080, 60), new DisplayModeInfo(1280, 960, 120));
        app.Factory.Configure = backend => backend.Displays.Add(Display(Dell, "DELL U2720Q"));
        app.ShowActive();
        return (app, (MainWindow)app.Window);
    }

    private static ComboBox ResolutionCombo(MainWindow window) =>
        window.GetVisualDescendants().OfType<ProfileSettingsView>().Single()
            .GetVisualDescendants().OfType<ComboBox>().Single(combo => combo.Classes.Contains("resolutionCombo"));

    private static void SelectRow(MainWindow window, int index)
    {
        window.GetVisualDescendants().OfType<ProfilesView>().Single().GetVisualDescendants().OfType<ListBox>().Single().SelectedIndex = index;
        Dispatcher.UIThread.RunJobs();
    }

    private static List<string> Texts(TestComposition app) => app.ProfileSettings.ResolutionItems.Select(item => item.Text).ToList();

    [AvaloniaFact]
    public void TheSelectorIsShownOnlyForApplicationProfiles()
    {
        var (app, window) = Show();
        using var _ = app;

        Assert.False(ResolutionCombo(window).IsVisible);

        SelectRow(window, 1);
        Assert.True(ResolutionCombo(window).IsVisible);
        Assert.Equal(["Don't change"], Texts(app));
        Assert.Equal("Don't change", app.ProfileSettings.SelectedResolutionItem!.Text);
        // Nothing is read until the list is opened.
        Assert.Empty(app.DisplayModes.Calls);
        window.Close();
    }

    [AvaloniaFact]
    public void OpeningTheListReadsTheModesOfTheSelectedDisplay()
    {
        var (app, window) = Show();
        using var _ = app;
        SelectRow(window, 1);

        ResolutionCombo(window).IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(app.ProfileSettings.IsResolutionListOpen);
        Assert.Equal(["Don't change", "2560 × 1440", "1920 × 1080", "1280 × 960"], Texts(app));
        Assert.Equal("Don't change", app.ProfileSettings.SelectedResolutionItem!.Text);
        // Opened again: read once.
        ResolutionCombo(window).IsDropDownOpen = false;
        ResolutionCombo(window).IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Single(app.DisplayModes.Calls, call => call.StartsWith("modes", StringComparison.Ordinal));
        window.Close();
    }

    // A display refresh starts a clean draft over; the open list keeps every resolution.
    [AvaloniaFact]
    public void ARefreshWhileTheListIsOpenKeepsEveryResolution()
    {
        var (app, window) = Show();
        using var _ = app;
        SelectRow(window, 1);
        ResolutionCombo(window).IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        var backend = app.Factory.Created[^1];
        var enumerations = backend.EnumerationCount;

        app.Host.DispatchMessage(0x007E, 0);
        Assert.True(TestComposition.RunUntil(() => backend.EnumerationCount > enumerations, TimeSpan.FromSeconds(3)));

        Assert.Equal(["Don't change", "2560 × 1440", "1920 × 1080", "1280 × 960"], Texts(app));
        Assert.Equal("Don't change", app.ProfileSettings.SelectedResolutionItem!.Text);
        window.Close();
    }

    [AvaloniaFact]
    public async Task AChosenResolutionIsSavedWithTheProfileAndLeavesTheColors()
    {
        var (app, window) = Show();
        using var _ = app;
        SelectRow(window, 1);
        app.ProfileSettings.IsResolutionListOpen = true;

        app.ProfileSettings.SelectedResolutionItem = app.ProfileSettings.ResolutionItems.Single(item => item.Text.StartsWith("1920", StringComparison.Ordinal));
        Assert.True(app.ProfileSettings.CanCancel);
        Assert.False(app.Editor.HasUnsavedColorChanges);
        await app.ProfileSettings.SaveCommand.ExecuteAsync(null);

        var game = app.Profiles.Profiles.Single(profile => profile.Name == "Game");
        Assert.Equal(new DisplayResolution(1920, 1080), game.DisplayProfiles[0].Resolution);
        Assert.Contains("\"Resolution\"", File.ReadAllText(app.ConfigPath));
        Assert.DoesNotContain(app.DisplayModes.Calls, call => call.StartsWith("apply", StringComparison.Ordinal));
        window.Close();
    }

    [AvaloniaFact]
    public async Task CancelRestoresTheSavedResolution()
    {
        var (app, window) = Show(Config(""","Resolution":{"Width":1280,"Height":960}"""));
        using var _ = app;
        SelectRow(window, 1);
        Assert.Equal("1280 × 960", app.ProfileSettings.SelectedResolutionItem!.Text);

        app.ProfileSettings.SelectedResolutionItem = app.ProfileSettings.ResolutionItems[0];
        Assert.True(app.ProfileSettings.CanCancel);
        await app.ProfileSettings.CancelCommand.ExecuteAsync(null);

        Assert.Equal("1280 × 960", app.ProfileSettings.SelectedResolutionItem!.Text);
        Assert.Equal(new DisplayResolution(1280, 960), app.Profiles.Profiles.Single(p => p.Name == "Game").DisplayProfiles[0].Resolution);
        window.Close();
    }

    [AvaloniaFact]
    public void ASavedResolutionTheDisplayDoesNotOfferIsKeptAndMarked()
    {
        var (app, window) = Show(Config(""","Resolution":{"Width":800,"Height":600}"""));
        using var _ = app;
        SelectRow(window, 1);

        app.ProfileSettings.IsResolutionListOpen = true;

        Assert.Equal("800 × 600 · not supported", app.ProfileSettings.SelectedResolutionItem!.Text);
        Assert.False(app.ProfileSettings.CanCancel);
        window.Close();
    }
}
