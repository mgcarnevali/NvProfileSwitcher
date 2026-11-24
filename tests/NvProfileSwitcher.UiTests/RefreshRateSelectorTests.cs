using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NvProfileSwitcher.Core;
using NvProfileSwitcher.Views;
using static NvProfileSwitcher.Core.Tests.FakeDisplayBackend;

namespace NvProfileSwitcher.UiTests;

// The Refresh rate selector of the profile editor, independent of the Resolution selector, over
// the composed application with one display (\\.\DISPLAY1) at 2560×1440 and 144 Hz that also
// offers 1920×1080 at 144 and 60 Hz and 1280×960 at 120 Hz.
public sealed class RefreshRateSelectorTests
{
    private const string Dell = "DEL4321-ABC123";
    private const string DellGdi = @"\\.\DISPLAY1";

    private static string Config(string mode = "") => $$"""
        {"Application Profiles":[
          {"Name":"Game","Exe Paths":["game.exe"],"Enabled":true,"Hotkey":0,
           "Display Profiles":[{"DisplayName":"DELL","MonitorId":"{{Dell}}","Brightness":100,"Contrast":100,"Gamma":1,"DigitalVibrance":50,"Hue":0{{mode}}}]}]}
        """;

    private static (TestComposition App, MainWindow Window) Show(string? config = null, double width = 1180, double height = 720)
    {
        var app = new TestComposition(
            config ?? Config(), createWindow: viewModel => new MainWindow { Width = width, Height = height, DataContext = viewModel });
        app.DisplayModes.AddMonitor(DellGdi, new DisplayModeInfo(2560, 1440, 144),
            new DisplayModeInfo(1920, 1080, 144), new DisplayModeInfo(1920, 1080, 60), new DisplayModeInfo(1280, 960, 120));
        app.Factory.Configure = backend => backend.Displays.Add(Display(Dell, "DELL U2720Q"));
        app.ShowActive();
        return (app, (MainWindow)app.Window);
    }

    private static T Editor<T>(MainWindow window, string cssClass) where T : Control =>
        window.GetVisualDescendants().OfType<ProfileSettingsView>().Single()
            .GetVisualDescendants().OfType<T>().Single(control => control.Classes.Contains(cssClass));

    private static void SelectRow(MainWindow window, int index)
    {
        window.GetVisualDescendants().OfType<ProfilesView>().Single().GetVisualDescendants().OfType<ListBox>().Single().SelectedIndex = index;
        Dispatcher.UIThread.RunJobs();
    }

    private static List<string> Rates(TestComposition app) => app.ProfileSettings.RefreshRateItems.Select(item => item.Text).ToList();

    private static void Choose(TestComposition app, string resolution) =>
        app.ProfileSettings.SelectedResolutionItem = app.ProfileSettings.ResolutionItems.Single(item => item.Text == resolution);

    private static void ChooseRate(TestComposition app, string text) =>
        app.ProfileSettings.SelectedRefreshRateItem = app.ProfileSettings.RefreshRateItems.Single(item => item.Text == text);

    private static JsonNode SavedValues(TestComposition app) =>
        JsonNode.Parse(File.ReadAllText(app.ConfigPath))!["Application Profiles"]![0]!["Display Profiles"]![0]!;

    [AvaloniaFact]
    public void TheSelectorIsShownOnlyForApplicationProfilesAndReadsNothingUntilOpened()
    {
        var (app, window) = Show();
        using var _ = app;
        Assert.False(Editor<ComboBox>(window, "refreshRateCombo").IsVisible);

        SelectRow(window, 1);

        Assert.True(Editor<ComboBox>(window, "refreshRateCombo").IsVisible);
        Assert.Equal(["Desktop rate", "Highest available"], Rates(app));
        Assert.Equal("Desktop rate", app.ProfileSettings.SelectedRefreshRateItem!.Text);
        Assert.Empty(app.DisplayModes.Calls);
        window.Close();
    }

    [AvaloniaFact]
    public void OpeningTheListShowsTheRatesOfTheDesktopResolutionAndWhatEachDefaultApplies()
    {
        var (app, window) = Show();
        using var _ = app;
        SelectRow(window, 1);

        Editor<ComboBox>(window, "refreshRateCombo").IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["Desktop rate · 144 Hz", "Highest available · 144 Hz", "144 Hz"], Rates(app));
        // Both lists come from one reading of the display's modes.
        app.ProfileSettings.IsResolutionListOpen = true;
        Assert.Single(app.DisplayModes.Calls, call => call.StartsWith("modes", StringComparison.Ordinal));
        window.Close();
    }

    [AvaloniaFact]
    public async Task TheRatesFollowTheResolutionAndBothAreSaved()
    {
        var (app, window) = Show();
        using var _ = app;
        SelectRow(window, 1);
        app.ProfileSettings.IsRefreshRateListOpen = true;

        Choose(app, "1920 × 1080");
        Assert.Equal(["Desktop rate · 144 Hz", "Highest available · 144 Hz", "144 Hz", "60 Hz"], Rates(app));
        ChooseRate(app, "60 Hz");
        await app.ProfileSettings.SaveCommand.ExecuteAsync(null);

        var saved = SavedValues(app);
        Assert.Equal(60, (int)saved["RefreshRate"]!);
        Assert.Equal(1920, (int)saved["Resolution"]!["Width"]!);
        window.Close();
    }

    [AvaloniaFact]
    public async Task ARateAloneIsSavedWithoutAResolution()
    {
        var (app, window) = Show();
        using var _ = app;
        SelectRow(window, 1);
        app.ProfileSettings.IsRefreshRateListOpen = true;

        ChooseRate(app, "Highest available · 144 Hz");
        Assert.True(app.ProfileSettings.CanCancel);
        await app.ProfileSettings.SaveCommand.ExecuteAsync(null);

        var saved = SavedValues(app);
        Assert.Equal("Highest", (string)saved["RefreshRate"]!);
        Assert.Null(saved["Resolution"]);
        window.Close();
    }

    // A saved rate the chosen resolution does not offer stays chosen and says so; it is never
    // replaced by another rate. Choosing a resolution that offers it shows it as offered again.
    [AvaloniaFact]
    public void ASavedRateTheResolutionDoesNotOfferIsKeptAndMarked()
    {
        var (app, window) = Show(Config(""","Resolution":{"Width":1280,"Height":960},"RefreshRate":144"""));
        using var _ = app;
        SelectRow(window, 1);
        Assert.Equal("144 Hz", app.ProfileSettings.SelectedRefreshRateItem!.Text);

        app.ProfileSettings.IsRefreshRateListOpen = true;
        Assert.Equal(["Desktop rate · 120 Hz", "Highest available · 120 Hz", "120 Hz", "144 Hz · not supported"], Rates(app));
        Assert.Equal("144 Hz · not supported", app.ProfileSettings.SelectedRefreshRateItem!.Text);
        Assert.False(app.ProfileSettings.CanCancel);

        Choose(app, "1920 × 1080");
        Assert.Equal("144 Hz", app.ProfileSettings.SelectedRefreshRateItem!.Text);
        window.Close();
    }

    [AvaloniaFact]
    public async Task CancelRestoresTheSavedRate()
    {
        var (app, window) = Show(Config(",\"RefreshRate\":\"Highest\""));
        using var _ = app;
        SelectRow(window, 1);
        Assert.Equal("Highest available", app.ProfileSettings.SelectedRefreshRateItem!.Text);

        ChooseRate(app, "Desktop rate");
        await app.ProfileSettings.CancelCommand.ExecuteAsync(null);

        Assert.Equal("Highest available", app.ProfileSettings.SelectedRefreshRateItem!.Text);
        window.Close();
    }

    // The three selectors fit the editor at every window size the layout tests cover.
    [AvaloniaTheory]
    [MemberData(nameof(MainWindowLayoutTests.Sizes), MemberType = typeof(MainWindowLayoutTests))]
    public void TheModeSelectorsFitTheEditor(string name, double width, double height)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Content fit is verified with the Windows system font.");
        var (app, window) = Show(width: width, height: height);
        using var _ = app;
        SelectRow(window, 1);
        window.UpdateLayout();

        var editor = window.GetVisualDescendants().OfType<ProfileSettingsView>().Single();
        var panel = editor.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("panel"));
        var inside = new Rect(panel.Bounds.Size).Deflate(panel.BorderThickness).Inflate(0.5);
        var combos = editor.GetVisualDescendants().OfType<ComboBox>().Where(c => c.IsEffectivelyVisible).ToList();
        foreach (var combo in combos)
            Assert.True(inside.Contains(new Rect(combo.TranslatePoint(new Point(0, 0), panel) ?? default, combo.Bounds.Size)),
                $"{name}: a selector leaves the editor");
        var display = combos.Single(c => !c.Classes.Contains("resolutionCombo") && !c.Classes.Contains("refreshRateCombo") && c.ItemsSource == app.ProfileSettings.Displays);
        Assert.True(display.Bounds.Width >= 150, $"{name}: the display selector is {display.Bounds.Width} wide");
        window.Close();
    }
}
