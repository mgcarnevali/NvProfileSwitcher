using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace NvProfileSwitcher.UiTests;

// The tray icon and menu of the original App, and the window states that show or hide it.
public sealed class TrayTests
{
    private static NativeMenuItem Item(TestComposition app, string header) =>
        app.Tray.Menu.Items.OfType<NativeMenuItem>().Single(item => item.Header == header);

    private static void Click(NativeMenuItem item) => ((INativeMenuItemExporterEventsImplBridge)item).RaiseClicked();

    [AvaloniaFact]
    public void MenuMatchesTheOriginal()
    {
        using var app = new TestComposition();

        Assert.Equal(
            ["Open NvProfileSwitcher", "-", "Global Switching", "-", "Check for updates", "About NvProfileSwitcher", "-", "Exit"],
            app.Tray.Menu.Items.Select(item => item is NativeMenuItem menuItem ? menuItem.Header : "-"));
        Assert.Equal(MenuItemToggleType.CheckBox, Item(app, "Global Switching").ToggleType);
        Assert.False(app.Tray.IsIconVisible);
    }

    // The menu and the status bar show the same switching state, also when it was loaded
    // or changed elsewhere.
    [AvaloniaFact]
    public void GlobalSwitchingItemFollowsTheSwitchingState()
    {
        using var app = new TestComposition(
            """{"RememberGlobalSwitching":true,"GlobalSwitchingEnabled":false}""");
        var item = Item(app, "Global Switching");
        Assert.False(item.IsChecked);

        app.AutomaticSwitching.SetGlobalEnabled(true);
        Assert.True(item.IsChecked);
        Assert.False(app.Switching.GlobalPaused);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void GlobalSwitchingItemTogglesAndSavesOnlyWhenRemembered(bool remember)
    {
        using var app = new TestComposition(remember ? """{"RememberGlobalSwitching":true}""" : null);
        app.Window.Show();
        var before = File.Exists(app.ConfigPath) ? File.ReadAllText(app.ConfigPath) : null;

        Click(Item(app, "Global Switching"));

        Assert.True(app.Switching.GlobalPaused);
        Assert.False(Item(app, "Global Switching").IsChecked);
        Assert.Equal("OFF", new NvProfileSwitcher.ViewModels.StatusBarViewModel(
            app.Nvidia, app.Switching, app.Dialogs).GlobalSwitchingText);
        if (remember)
            Assert.Contains("\"GlobalSwitchingEnabled\": false", File.ReadAllText(app.ConfigPath));
        else
            Assert.Equal(before, File.Exists(app.ConfigPath) ? File.ReadAllText(app.ConfigPath) : null);

        Click(Item(app, "Global Switching"));
        Assert.False(app.Switching.GlobalPaused);
        Assert.True(Item(app, "Global Switching").IsChecked);
    }

    [AvaloniaFact]
    public void OpenShowsTheHiddenWindowAndHidesTheIcon()
    {
        using var app = new TestComposition();
        app.Window.Show();
        app.Host.HideToTray();
        Assert.True(app.Tray.IsIconVisible);
        Assert.False(app.Window.IsVisible);

        Click(Item(app, "Open NvProfileSwitcher"));

        Assert.True(app.Window.IsVisible);
        Assert.True(app.Window.ShowInTaskbar);
        Assert.False(app.Tray.IsIconVisible);
    }

    // A second instance asks from another thread; the window is shown on the UI thread.
    [AvaloniaFact]
    public void ShowRequestFromASecondInstanceShowsTheWindow()
    {
        using var app = new TestComposition();
        app.Window.Show();
        app.Host.HideToTray();

        Task.Run(app.Tray.PostShowMainWindow).Wait();
        Assert.False(app.Window.IsVisible);
        Dispatcher.UIThread.RunJobs();

        Assert.True(app.Window.IsVisible);
        Assert.False(app.Tray.IsIconVisible);
    }

    [AvaloniaFact]
    public void ExitHidesTheIconAndShutsDown()
    {
        using var app = new TestComposition();
        app.Tray.SetIconVisible(true);

        Click(Item(app, "Exit"));

        Assert.False(app.Tray.IsIconVisible);
        Assert.Equal(1, app.ShutdownRequests);
    }

    [AvaloniaFact]
    public void CheckForUpdatesRunsTheManualCheckAfterTheClick()
    {
        using var app = new TestComposition();

        Click(Item(app, "Check for updates"));
        Assert.Equal(0, app.Releases.InfoRequests);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, app.Releases.InfoRequests);
        Assert.Single(app.Dialogs.UpdatesShown);
    }

    [AvaloniaFact]
    public void CheckForUpdatesInADevelopmentBuildReportsThatChecksAreDisabled()
    {
        using var app = new TestComposition(isDevelopmentBuild: true);

        Click(Item(app, "Check for updates"));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal([("Check for updates", "Update checks are disabled in development builds.")], app.Dialogs.Messages);
        Assert.Equal(0, app.Releases.InfoRequests);
    }

    [AvaloniaFact]
    public void AboutShowsTheAboutDialogAfterTheClick()
    {
        using var app = new TestComposition();

        Click(Item(app, "About NvProfileSwitcher"));
        Assert.Equal(0, app.Dialogs.AboutCount);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, app.Dialogs.AboutCount);
    }

    [AvaloniaFact]
    public void MinimizingWithMinimizeToTrayHidesTheWindowToTheTray()
    {
        using var app = new TestComposition();
        app.Settings.MinimizeToTray = true;
        app.Window.Show();

        app.Window.WindowState = WindowState.Minimized;

        Assert.False(app.Window.IsVisible);
        Assert.False(app.Window.ShowInTaskbar);
        Assert.True(app.Tray.IsIconVisible);
    }

    [AvaloniaFact]
    public void MinimizingWithoutMinimizeToTrayHidesTheIcon()
    {
        using var app = new TestComposition();
        app.Window.Show();
        app.Tray.SetIconVisible(true);

        app.Window.WindowState = WindowState.Minimized;

        Assert.True(app.Window.IsVisible);
        Assert.True(app.Window.ShowInTaskbar);
        Assert.False(app.Tray.IsIconVisible);
    }
}
