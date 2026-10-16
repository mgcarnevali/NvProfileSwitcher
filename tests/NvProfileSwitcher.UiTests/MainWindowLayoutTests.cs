using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NvProfileSwitcher.Views;

namespace NvProfileSwitcher.UiTests;

public sealed class MainWindowLayoutTests
{
    private const double WindowPadding = 18;

    // Horizontal margin of the status bar inside the window (4 on each side).
    private const double StatusBarMargin = 8;

    // Window client sizes in DIP, including the sizes on both sides of every breakpoint
    // (content width 1144 and 684, i.e. window width 1180 and 720). The scaled entries are
    // the DIP area of a 1920x1080 screen at that Windows scaling.
    public static TheoryData<string, double, double> Sizes => new()
    {
        { "reference-1180x720", 1180, 720 },
        { "large-1600x1000", 1600, 1000 },
        { "breakpoint-1181x720", 1181, 720 },
        { "breakpoint-1179x720", 1179, 720 },
        { "wide-short-1180x600", 1180, 600 },
        { "medium-1000x700", 1000, 700 },
        { "medium-800x600", 800, 600 },
        { "breakpoint-721x600", 721, 600 },
        { "breakpoint-720x600", 720, 600 },
        { "breakpoint-719x600", 719, 600 },
        { "small-640x480", 640, 480 },
        { "1080p-at-125-1536x864", 1536, 864 },
        { "1080p-at-150-1280x720", 1280, 720 },
        { "1080p-at-200-960x540", 960, 540 },
    };

    private static int ExpectedColumns(double windowWidth) =>
        (windowWidth - 2 * WindowPadding) switch { >= 1144 => 3, >= 684 => 2, _ => 1 };

    // The status bar shows its widest live values: "Unavailable" and an "Unknown" driver.
    private static MainWindow Show(double width, double height)
    {
        var window = new MainWindow { Width = width, Height = height, DataContext = TestViewModels.MainWindow() };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static T Single<T>(Visual root) where T : Visual =>
        root.GetVisualDescendants().OfType<T>().Single();

    private static List<Border> Panels(Visual root) =>
        root.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("panel")).ToList();

    private static Rect BoundsIn(Visual visual, Visual ancestor) =>
        new(visual.TranslatePoint(new Point(0, 0), ancestor) ?? default, visual.Bounds.Size);

    [AvaloniaFact]
    public void MainWindowOnlyComposesTheSectionViews()
    {
        var window = Show(1180, 720);

        Single<ProfilesView>(window);
        Single<ProfileSettingsView>(window);
        Single<HotkeysView>(window);
        Single<ApplicationSettingsView>(window);
        Single<StatusBarView>(window);
        Assert.Equal(4, Panels(window).Count);
        window.Close();
    }

    [AvaloniaTheory]
    [MemberData(nameof(Sizes))]
    public void PanelsAreReorganizedForTheAvailableWidth(string name, double width, double height)
    {
        var window = Show(width, height);
        var body = window.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Classes.Contains("body"));
        var grid = (Grid)body.Content!;
        var profiles = BoundsIn(Single<ProfilesView>(window), grid);
        var settings = BoundsIn(Single<ProfileSettingsView>(window), grid);
        var hotkeys = BoundsIn(Single<HotkeysView>(window), grid);
        var application = BoundsIn(Single<ApplicationSettingsView>(window), grid);

        switch (ExpectedColumns(width))
        {
            case 3:
                Assert.True(settings.Left > profiles.Right && hotkeys.Left > settings.Right, $"{name}: not three columns");
                Assert.True(application.Top > hotkeys.Bottom && Math.Abs(application.Left - hotkeys.Left) < 0.5, $"{name}: right column");
                var advanced = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Advanced..."));
                var save = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Save profile"));
                Assert.True(Math.Abs(BoundsIn(advanced, grid).Bottom - BoundsIn(save, grid).Bottom) < 0.5, $"{name}: Advanced not aligned with Save profile");
                break;
            case 2:
                Assert.True(settings.Left > profiles.Right && Math.Abs(settings.Top - profiles.Top) < 0.5, $"{name}: not two columns");
                Assert.True(hotkeys.Top > Math.Max(profiles.Bottom, settings.Bottom), $"{name}: Hotkeys not below");
                Assert.True(application.Left > hotkeys.Right && Math.Abs(application.Top - hotkeys.Top) < 0.5, $"{name}: bottom row");
                break;
            default:
                Assert.True(settings.Top > profiles.Bottom && hotkeys.Top > settings.Bottom && application.Top > hotkeys.Bottom,
                    $"{name}: not one column");
                foreach (var panel in new[] { profiles, settings, hotkeys, application })
                    Assert.True(Math.Abs(panel.Width - grid.Bounds.Width) < 0.5, $"{name}: panel not full width");
                break;
        }

        // No horizontal scrolling and nothing outside the width.
        Assert.True(body.Extent.Width <= body.Viewport.Width + 0.5, $"{name}: horizontal overflow");
        var panels = Panels(window);
        foreach (var panel in panels)
        {
            var rect = BoundsIn(panel, grid);
            Assert.True(rect.Left >= -0.5 && rect.Right <= grid.Bounds.Width + 0.5, $"{name}: panel outside the width");
            foreach (var other in panels.Where(p => p != panel))
                Assert.False(rect.Intersects(BoundsIn(other, grid)), $"{name}: panels overlap");
        }

        // Three columns with the original body height fit the window without scrolling;
        // the other layouts scroll the body vertically when needed.
        var expectedScrolling = ExpectedColumns(width) == 3 && body.Bounds.Height >= 571
            ? ScrollBarVisibility.Disabled
            : ScrollBarVisibility.Auto;
        Assert.Equal(expectedScrolling, body.VerticalScrollBarVisibility);
        if (expectedScrolling == ScrollBarVisibility.Disabled)
            Assert.True(body.Extent.Height <= body.Viewport.Height + 0.5, $"{name}: unexpected vertical scrolling");

        var directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(directory);
        window.CaptureRenderedFrame()?.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
        window.Close();
    }

    [AvaloniaTheory]
    [MemberData(nameof(Sizes))]
    public void HeaderAndStatusBarStayVisible(string name, double width, double height)
    {
        var window = Show(width, height);
        var client = new Rect(0, 0, width, height).Inflate(0.5);
        var header = BoundsIn(window.GetVisualDescendants().OfType<Image>().First(), window);
        var statusBar = Single<StatusBarView>(window);

        Assert.True(client.Contains(header), $"{name}: header outside the window");
        Assert.True(client.Contains(BoundsIn(statusBar, window)), $"{name}: status bar outside the window");
        foreach (var group in statusBar.GetVisualDescendants().OfType<StackPanel>().Take(2))
            Assert.True(client.Contains(BoundsIn(group, window)), $"{name}: status bar content outside the window");
        window.Close();
    }

    // Text measurement depends on the font, so content fit is verified with the app's
    // real font (Segoe UI), on Windows.
    [AvaloniaTheory]
    [MemberData(nameof(Sizes))]
    public void NothingIsClippedOrLeavesItsPanel(string name, double width, double height)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Content fit is verified with the Windows system font.");
        var window = Show(width, height);

        foreach (var panel in Panels(window))
        {
            // Visible controls stay inside the panel border. Excluded: the profile list's
            // items (the list scrolls them), the inside of a Viewbox (laid out unscaled; the
            // Viewbox itself is checked) and closed popups, which take no layout space.
            var inside = new Rect(panel.Bounds.Size).Deflate(panel.BorderThickness).Inflate(0.5);
            foreach (var control in panel.GetVisualDescendants().OfType<Control>()
                         .Where(c => c is not Popup && c.IsEffectivelyVisible && c.Bounds.Width > 0 && c.Bounds.Height > 0
                                     && c.FindAncestorOfType<ListBox>() is null
                                     && c.FindAncestorOfType<Viewbox>() is null))
            {
                Assert.True(inside.Contains(BoundsIn(control, panel)),
                    $"{name}: {control.GetType().Name} '{(control as TextBlock)?.Text}' leaves its panel");
            }
        }

        foreach (var text in window.GetVisualDescendants().OfType<TextBlock>()
                     .Where(t => t.IsEffectivelyVisible && t.TextTrimming == TextTrimming.None && !string.IsNullOrEmpty(t.Text)))
            Assert.True(text.Bounds.Width + 0.5 >= text.DesiredSize.Width - text.Margin.Left - text.Margin.Right,
                $"{name}: '{text.Text}' is clipped");

        window.Close();
    }

    // Status texts that set the status bar width: the initial values, the longest fixed
    // active profile text, an override and a name trimmed to the active profile MaxWidth.
    // The NVIDIA API state is the widest one ("Unavailable" and an "Unknown" driver).
    public static TheoryData<string, string> StatusTexts => new()
    {
        { "Windows", "ON" },
        { "Windows (override)", "OFF" },
        { "Counter-Strike 2 (override)", "OFF" },
        { "A profile name that is quite long, sixty characters in total", "OFF" },
    };

    // The texts are set on the controls to cover the widest case, including combinations the
    // switching state does not show together (a trimmed name with "OFF").
    private static void SetStatusTexts(MainWindow window, string activeProfile, string globalSwitching)
    {
        var texts = Single<StatusBarView>(window).GetVisualDescendants().OfType<TextBlock>().ToList();
        texts[texts.FindIndex(t => t.Text == "Active profile:") + 1].Text = activeProfile;
        texts[texts.FindIndex(t => t.Text == "Global Switching:") + 1].Text = globalSwitching;
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    // The status values and the links never overlap or leave the window, whatever the
    // layout the status bar width selects.
    [AvaloniaTheory]
    [MemberData(nameof(Sizes))]
    public void StatusValuesAndLinksDoNotOverlap(string name, double width, double height)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Content fit is verified with the Windows system font.");
        foreach (var row in StatusTexts)
        {
            var (activeProfile, globalSwitching) = row.Data;
            var window = Show(width, height);
            SetStatusTexts(window, activeProfile, globalSwitching);
            AssertStatusValuesAndLinksDoNotOverlap(window, $"{name}, '{activeProfile}' {globalSwitching}");
            window.Close();
        }
    }

    // Status bar widths on both sides of the two container query breakpoints. Above 896 DIP
    // everything shares one line; up to 896 the links move to the second line; up to 726
    // the active profile and global switching also move to the second line, without their
    // leading separator. The breakpoints are the widths the widest values need (measured
    // with Segoe UI), so every status text must fit at every boundary.
    [AvaloniaTheory]
    [InlineData(897, false, false)]
    [InlineData(896, true, false)]
    [InlineData(895, true, false)]
    [InlineData(727, true, false)]
    [InlineData(726, true, true)]
    [InlineData(725, true, true)]
    public void StatusBarBreakpointsKeepValuesAndLinksApart(double statusBarWidth, bool linksBelow, bool switchingBelow)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Content fit is verified with the Windows system font.");
        foreach (var row in StatusTexts)
        {
            var (activeProfile, globalSwitching) = row.Data;
            var name = $"status bar {statusBarWidth}, '{activeProfile}' {globalSwitching}";
            var window = Show(statusBarWidth + 2 * WindowPadding + StatusBarMargin, 600);
            SetStatusTexts(window, activeProfile, globalSwitching);
            var grid = Single<StatusBarView>(window).GetVisualDescendants().OfType<Grid>().First();
            var switching = StatusGroup(window, "switchingStatus");
            var leadingSeparator = switching.Children.OfType<Border>().First();

            Assert.Equal(statusBarWidth, grid.Bounds.Width);
            Assert.Equal(linksBelow ? 1 : 0, Grid.GetRow(StatusGroup(window, "links")));
            Assert.Equal(switchingBelow ? 1 : 0, Grid.GetRow(switching));
            Assert.Equal(!switchingBelow, leadingSeparator.IsVisible);
            AssertStatusValuesAndLinksDoNotOverlap(window, name);
            window.Close();
        }
    }

    private static StackPanel StatusGroup(MainWindow window, string group) =>
        Single<StatusBarView>(window).GetVisualDescendants().OfType<StackPanel>().Single(p => p.Classes.Contains(group));

    private static void AssertStatusValuesAndLinksDoNotOverlap(MainWindow window, string name)
    {
        var client = new Rect(window.ClientSize);

        IEnumerable<(string Text, Rect Bounds)> Items(string group) =>
            StatusGroup(window, group).Children
                .Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0)
                .Select(c => ((c as TextBlock)?.Text ?? c.GetType().Name, BoundsIn(c, window)))
                .ToList();

        var nvidia = Items("nvidiaStatus");
        var switching = Items("switchingStatus");
        var links = Items("links");

        foreach (var value in nvidia.Concat(switching))
        {
            Assert.True(client.Contains(value.Bounds), $"{name}: status value '{value.Text}' outside the window");
            foreach (var link in links)
                Assert.False(value.Bounds.Intersects(link.Bounds),
                    $"{name}: status value '{value.Text}' overlaps link '{link.Text}'");
        }

        foreach (var value in switching)
            foreach (var other in nvidia)
                Assert.False(value.Bounds.Intersects(other.Bounds),
                    $"{name}: status value '{value.Text}' overlaps '{other.Text}'");
    }
}
