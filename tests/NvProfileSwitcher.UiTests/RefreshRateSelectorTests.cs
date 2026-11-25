using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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

    // ---- The "Fixed refresh rate" caption ----

    private static ComboBox RateCombo(MainWindow window) => Editor<ComboBox>(window, "refreshRateCombo");

    private static ComboBoxItem Container(MainWindow window, int index) =>
        Assert.IsType<ComboBoxItem>(RateCombo(window).ContainerFromIndex(index));

    // Chooses a rate in the selector itself, as the user does.
    private static void ChooseInSelector(TestComposition app, MainWindow window, string text)
    {
        RateCombo(window).SelectedItem = app.ProfileSettings.RefreshRateItems.Single(item => item.Text == text);
        Dispatcher.UIThread.RunJobs();
    }

    private static TextBlock? Caption(Control container) =>
        container.GetVisualDescendants().OfType<TextBlock>().SingleOrDefault(t => t.Classes.Contains("refreshRateHeader") && t.IsEffectivelyVisible);

    // The caption is drawn above the first fixed rate only, and only when there are fixed rates;
    // every item in the list is a rate, enabled, with nothing between them to land on.
    [AvaloniaFact]
    public void TheCaptionIsShownAboveTheFirstFixedRateOnly()
    {
        var (app, window) = Show();
        using var _ = app;
        SelectRow(window, 1);
        Assert.DoesNotContain(app.ProfileSettings.RefreshRateItems, item => item.StartsFixedRates);

        RateCombo(window).IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal([false, false, true], app.ProfileSettings.RefreshRateItems.Select(item => item.StartsFixedRates));
        Assert.Null(Caption(Container(window, 0)));
        Assert.Null(Caption(Container(window, 1)));
        Assert.Equal("Fixed refresh rate", Caption(Container(window, 2))!.Text);
        Assert.All(Enumerable.Range(0, app.ProfileSettings.RefreshRateItems.Count), i => Assert.True(Container(window, i).IsEnabled));
        window.Close();
    }

    // Chosen, the first fixed rate shows its rate alone, without the caption.
    [AvaloniaFact]
    public void TheSelectedFirstFixedRateShowsTheRateAlone()
    {
        var (app, window) = Show();
        using var _ = app;
        SelectRow(window, 1);
        app.ProfileSettings.IsRefreshRateListOpen = true;

        ChooseInSelector(app, window, "144 Hz");

        Assert.True(app.ProfileSettings.SelectedRefreshRateItem!.StartsFixedRates);
        var presenter = RateCombo(window).GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().First(p => ReferenceEquals(p.Content, RateCombo(window).SelectionBoxItem));
        Assert.Null(Caption(presenter));
        Assert.Contains(presenter.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "144 Hz");
        window.Close();
    }

    // The arrow keys move from the automatic rates to the fixed ones and back, one item at a time.
    [AvaloniaFact]
    public void TheKeyboardMovesBetweenRatesWithoutStopping()
    {
        var (app, window) = Show();
        using var _ = app;
        SelectRow(window, 1);
        app.ProfileSettings.IsRefreshRateListOpen = true;
        app.ProfileSettings.IsRefreshRateListOpen = false;
        ChooseInSelector(app, window, "Highest available · 144 Hz");
        RateCombo(window).Focus();
        Dispatcher.UIThread.RunJobs();

        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("144 Hz", app.ProfileSettings.SelectedRefreshRateItem!.Text);
        Assert.Same(app.ProfileSettings.SelectedRefreshRateItem, RateCombo(window).SelectedItem);

        window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Highest available · 144 Hz", app.ProfileSettings.SelectedRefreshRateItem!.Text);
        Assert.Same(app.ProfileSettings.SelectedRefreshRateItem, RateCombo(window).SelectedItem);
        window.Close();
    }

    // Saved profiles load with their rate chosen, before and after the list is read.
    [AvaloniaTheory]
    [InlineData("", "Desktop rate", "Desktop rate · 144 Hz")]
    [InlineData(",\"RefreshRate\":\"Highest\"", "Highest available", "Highest available · 144 Hz")]
    [InlineData(",\"RefreshRate\":144", "144 Hz", "144 Hz")]
    [InlineData(",\"RefreshRate\":75", "75 Hz", "75 Hz · not supported")]
    public void SavedRatesLoadChosenWithTheHeader(string rate, string beforeOpening, string afterOpening)
    {
        var (app, window) = Show(Config(rate));
        using var _ = app;
        SelectRow(window, 1);
        Assert.Equal(beforeOpening, app.ProfileSettings.SelectedRefreshRateItem!.Text);
        Assert.Same(app.ProfileSettings.SelectedRefreshRateItem, RateCombo(window).SelectedItem);

        app.ProfileSettings.IsRefreshRateListOpen = true;

        Assert.Equal(afterOpening, app.ProfileSettings.SelectedRefreshRateItem!.Text);
        Assert.Same(app.ProfileSettings.SelectedRefreshRateItem, RateCombo(window).SelectedItem);
        Assert.False(app.ProfileSettings.CanCancel);
        window.Close();
    }

    // ---- The display selector ----

    // The selected display is trimmed with an ellipsis; the list and the tooltip keep the full label.
    [AvaloniaFact]
    public void TheSelectedDisplayIsTrimmedAndItsFullLabelIsKept()
    {
        var (app, window) = Show();
        using var _ = app;
        SelectRow(window, 1);
        var display = window.GetVisualDescendants().OfType<ProfileSettingsView>().Single()
            .GetVisualDescendants().OfType<ComboBox>().Single(c => ReferenceEquals(c.ItemsSource, app.ProfileSettings.Displays));
        var label = app.ProfileSettings.SelectedDisplay!.SelectorLabel;

        Assert.Equal(label, ToolTip.GetTip(display));
        var selection = display.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == label);
        Assert.Equal(Avalonia.Media.TextTrimming.CharacterEllipsis, selection.TextTrimming);

        display.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        var listed = Assert.IsType<ComboBoxItem>(display.ContainerFromIndex(0)).GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == label);
        Assert.Equal(Avalonia.Media.TextTrimming.None, listed.TextTrimming);
        window.Close();
    }

    // The three selectors are visible, usable and inside the editor panel at every window size
    // the layout tests cover: without overlapping, with their labels unclipped, and the display
    // selector keeping a usable width.
    [AvaloniaTheory]
    [MemberData(nameof(MainWindowLayoutTests.Sizes), MemberType = typeof(MainWindowLayoutTests))]
    public void TheModeSelectorsFitTheEditor(string name, double width, double height)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Content fit is verified with the Windows system font.");
        var (app, window) = Show(width: width, height: height);
        using var _ = app;
        SelectRow(window, 1);
        window.UpdateLayout();

        // The panel border is the root of the editor view, so it is looked for inside it.
        var editor = window.GetVisualDescendants().OfType<ProfileSettingsView>().Single();
        var panel = editor.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("panel"));
        var inside = new Rect(panel.Bounds.Size).Deflate(panel.BorderThickness).Inflate(0.5);
        Rect BoundsInPanel(Visual visual) => new(visual.TranslatePoint(new Point(0, 0), panel) ?? default, visual.Bounds.Size);

        var display = editor.GetVisualDescendants().OfType<ComboBox>().Single(c => ReferenceEquals(c.ItemsSource, app.ProfileSettings.Displays));
        var selectors = new[] { display, Editor<ComboBox>(window, "resolutionCombo"), Editor<ComboBox>(window, "refreshRateCombo") };
        foreach (var selector in selectors)
        {
            Assert.True(selector.IsEffectivelyVisible && selector.IsEffectivelyEnabled, $"{name}: a selector cannot be used");
            Assert.True(inside.Contains(BoundsInPanel(selector)), $"{name}: a selector leaves the editor");
        }

        // Side by side when the editor is wide, Resolution and Refresh rate below the display
        // selector when it is narrow: never on top of each other.
        for (var i = 0; i < selectors.Length; i++)
            for (var j = i + 1; j < selectors.Length; j++)
                Assert.False(BoundsInPanel(selectors[i]).Deflate(0.5).Intersects(BoundsInPanel(selectors[j]).Deflate(0.5)), $"{name}: selectors overlap");
        Assert.True(display.Bounds.Width >= 150, $"{name}: the display selector is {display.Bounds.Width} wide");

        foreach (var label in editor.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Text is "Display" or "Resolution" or "Refresh rate"))
        {
            Assert.True(label.IsEffectivelyVisible, $"{name}: '{label.Text}' is hidden");
            Assert.True(label.Bounds.Width + 0.5 >= label.DesiredSize.Width - label.Margin.Left - label.Margin.Right, $"{name}: '{label.Text}' is clipped");
        }

        window.Close();
    }
}
