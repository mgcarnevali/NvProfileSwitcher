using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NvProfileSwitcher.Core.Tests;
using NvProfileSwitcher.Models;
using NvProfileSwitcher.Services;
using NvProfileSwitcher.Views;

namespace NvProfileSwitcher.UiTests;

// The NVIDIA API status and the driver version shown with the original colors and values.
public sealed class StatusBarViewTests
{
    private static readonly Color Available = Color.Parse("#52D627");
    private static readonly Color Unavailable = Color.Parse("#E84B4B");
    private static readonly Color GlobalOff = Color.Parse("#E35D6A");

    private static (Window Window, StatusBarView Bar) Show(TestComposition app)
    {
        var window = new MainWindow { DataContext = app.ViewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, window.GetVisualDescendants().OfType<StatusBarView>().Single());
    }

    private static Ellipse Dot(StatusBarView bar) =>
        bar.GetVisualDescendants().OfType<Ellipse>().Single(e => e.Classes.Contains("nvApiStatus"));

    private static TextBlock StatusText(StatusBarView bar) =>
        bar.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("nvApiStatus"));

    // The value right after the "Driver" label.
    private static TextBlock DriverText(StatusBarView bar)
    {
        var texts = bar.GetVisualDescendants().OfType<TextBlock>().ToList();
        return texts[texts.FindIndex(t => t.Text == "Driver") + 1];
    }

    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    [AvaloniaFact]
    public void InitialStateMatchesTheOriginalStatusBar()
    {
        var (window, bar) = Show(TestViewModels.Compose());

        Assert.Equal("Available", StatusText(bar).Text);
        Assert.Equal("Unknown", DriverText(bar).Text);
        Assert.Equal(Available, ColorOf(Dot(bar).Fill));
        window.Close();
    }

    [AvaloniaFact]
    public void AvailableApiShowsTheDriverVersionInGreen()
    {
        var app = TestViewModels.Compose();
        app.Factory.Configure = backend => backend.DriverVersion = "581.57";
        var (window, bar) = Show(app);

        app.Nvidia.Initialize();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Available", StatusText(bar).Text);
        Assert.Equal(Available, ColorOf(StatusText(bar).Foreground));
        Assert.Equal(Available, ColorOf(Dot(bar).Fill));
        Assert.Equal("581.57", DriverText(bar).Text);
        window.Close();
    }

    [AvaloniaFact]
    public void UnavailableApiIsShownInRedWithAnUnknownDriver()
    {
        var app = TestViewModels.Compose();
        var (window, bar) = Show(app);

        app.Nvidia.ReportUnavailable();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Unavailable", StatusText(bar).Text);
        Assert.Equal(Unavailable, ColorOf(StatusText(bar).Foreground));
        Assert.Equal(Unavailable, ColorOf(Dot(bar).Fill));
        Assert.Equal("Unknown", DriverText(bar).Text);
        window.Close();
    }

    // As in the original status bar, the values are not dimmed like their labels.
    [AvaloniaFact]
    public void StatusValuesAreNotDimmed()
    {
        var (window, bar) = Show(TestViewModels.Compose());

        Assert.Equal(1, StatusText(bar).Opacity);
        Assert.Equal(1, DriverText(bar).Opacity);
        window.Close();
    }

    private static TextBlock ActiveProfileText(StatusBarView bar) =>
        bar.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("activeProfile"));

    private static TextBlock ShowActiveProfile(StatusBarView bar, Window window, string text)
    {
        var active = ActiveProfileText(bar);
        active.Text = text;
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return active;
    }

    // A long active profile is trimmed with an ellipsis at its MaxWidth and its full text is
    // shown in the tooltip.
    [AvaloniaFact]
    public void LongActiveProfileIsTrimmedAndShownInFullInTheTooltip()
    {
        const string longName = "A profile name that is quite long, sixty characters in total";
        var (window, bar) = Show(TestViewModels.Compose());

        var active = ShowActiveProfile(bar, window, longName);

        Assert.True(active.TextLayout.TextLines[0].HasCollapsed);
        Assert.True(active.Bounds.Width <= 160, $"active profile width {active.Bounds.Width}");
        Assert.Equal(longName, ToolTip.GetTip(active));
        window.Close();
    }

    // The longest fixed active profile text and a typical override fit without trimming.
    [AvaloniaTheory]
    [InlineData("Windows (override)")]
    [InlineData("Counter-Strike 2 (override)")]
    public void TypicalActiveProfilesAreNotTrimmed(string text)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Text width is verified with the Windows system font.");
        var (window, bar) = Show(TestViewModels.Compose());

        var active = ShowActiveProfile(bar, window, text);

        Assert.False(active.TextLayout.TextLines[0].HasCollapsed, $"'{text}' is trimmed");
        Assert.Equal(text, ToolTip.GetTip(active));
        window.Close();
    }

    private static TextBlock GlobalSwitchingText(StatusBarView bar) =>
        bar.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("globalSwitching"));

    // The original status bar starts with "Windows" and global switching on, values not dimmed.
    [AvaloniaFact]
    public void InitialSwitchingStatusMatchesTheOriginal()
    {
        var (window, bar) = Show(TestViewModels.Compose());

        Assert.Equal("Windows", ActiveProfileText(bar).Text);
        Assert.Equal("ON", GlobalSwitchingText(bar).Text);
        Assert.Equal(Available, ColorOf(GlobalSwitchingText(bar).Foreground));
        Assert.Equal(1, ActiveProfileText(bar).Opacity);
        Assert.Equal(1, GlobalSwitchingText(bar).Opacity);
        window.Close();
    }

    [AvaloniaFact]
    public void PublishedActiveProfileIsShown()
    {
        var app = TestViewModels.Compose();
        var switching = app.Switching;
        var (window, bar) = Show(app);

        switching.DisplayedActiveProfile = new ActiveProfileDisplay("Game", OverrideMode.Profile, false);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Game (override)", ActiveProfileText(bar).Text);
        Assert.Equal("Game (override)", ToolTip.GetTip(ActiveProfileText(bar)));
        window.Close();
    }

    // Global switching follows the state at once; the active profile changes only when it is
    // published, as the original footer did.
    [AvaloniaFact]
    public void GlobalSwitchingFollowsTheStateAndTheActiveProfileWaitsForPublication()
    {
        var app = TestViewModels.Compose();
        var switching = app.Switching;
        var (window, bar) = Show(app);
        switching.DisplayedActiveProfile = new ActiveProfileDisplay("Game", OverrideMode.Automatic, false);
        Dispatcher.UIThread.RunJobs();

        switching.GlobalPaused = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("OFF", GlobalSwitchingText(bar).Text);
        Assert.Equal(GlobalOff, ColorOf(GlobalSwitchingText(bar).Foreground));
        Assert.Equal("Game", ActiveProfileText(bar).Text);

        switching.DisplayedActiveProfile = new ActiveProfileDisplay("Game", OverrideMode.Automatic, true);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Windows", ActiveProfileText(bar).Text);
        window.Close();
    }
}
