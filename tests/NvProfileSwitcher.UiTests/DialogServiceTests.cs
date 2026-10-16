using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NvProfileSwitcher.Core.Tests;
using NvProfileSwitcher.Shell;

namespace NvProfileSwitcher.UiTests;

// The dialogs shown by DialogService over the main window: the original message dialog and
// the copied update and About dialogs.
public sealed class DialogServiceTests
{
    private static (Window Main, DialogService Dialogs) Create()
    {
        var host = new MainWindowHost();
        var main = new Window();
        host.Attach(main);
        return (main, new DialogService(host));
    }

    private static T OwnedDialog<T>(Window main) where T : Window =>
        Assert.IsType<T>(Assert.Single(main.OwnedWindows));

    [AvaloniaFact]
    public void MessageDialogMatchesTheOriginal()
    {
        var (main, dialogs) = Create();
        main.Show();

        var shown = dialogs.ShowMessageAsync("Check for updates", "Could not check for updates.");
        Dispatcher.UIThread.RunJobs();

        var dialog = OwnedDialog<CenteredDialogWindow>(main);
        Assert.Equal("Check for updates", dialog.Title);
        Assert.Equal(500, dialog.Width);
        Assert.Equal(SizeToContent.Height, dialog.SizeToContent);
        Assert.False(dialog.CanResize);
        var panel = Assert.IsType<StackPanel>(dialog.Content);
        Assert.Equal(new Avalonia.Thickness(22), panel.Margin);
        Assert.Equal(20, panel.Spacing);
        var message = Assert.IsType<TextBlock>(panel.Children[0]);
        Assert.Equal("Could not check for updates.", message.Text);
        Assert.Contains("dialogMessage", message.Classes);
        var ok = Assert.IsType<Button>(panel.Children[1]);
        Assert.Equal("OK", ok.Content);
        Assert.Equal(100, ok.MinWidth);

        ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(TestComposition.RunUntil(() => shown.IsCompleted, TimeSpan.FromSeconds(5)));
        Assert.Empty(main.OwnedWindows);
        main.Close();
    }

    // Hotkey messages use the same dialog: modal over the visible main window, standalone
    // and centered on screen while it is minimized or hidden (deliberate deviation: the
    // original was always modal).
    [AvaloniaTheory]
    [InlineData("Normal")]
    [InlineData("Minimized")]
    [InlineData("Hidden")]
    public void HotkeyMessagesAreModalOnlyOverAVisibleMainWindow(string state)
    {
        var (main, dialogs) = Create();
        var hotkeys = new NvProfileSwitcher.Services.HotkeyService(
            new FakeHotkeyRegistrar(), new NvProfileSwitcher.Models.AppSettings(), new NvProfileSwitcher.Services.ProfileStore(new NvProfileSwitcher.Models.SwitchingState()), dialogs);
        if (state != "Hidden")
            main.Show();
        if (state == "Minimized")
            main.WindowState = WindowState.Minimized;
        var opened = new List<Window>();
        using var subscription = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => opened.Add(window));

        _ = hotkeys.ShowMessageAsync("That hotkey is already in use by another application.");
        Dispatcher.UIThread.RunJobs();

        var dialog = Assert.Single(opened);
        Assert.Equal("NvProfileSwitcher", dialog.Title);
        Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Text == "That hotkey is already in use by another application.");
        if (state == "Normal")
        {
            Assert.Same(main, dialog.Owner);
        }
        else
        {
            Assert.Null(dialog.Owner);
            Assert.Equal(WindowStartupLocation.CenterScreen, dialog.WindowStartupLocation);
        }

        Assert.True(hotkeys.IsMessageDialogOpen);
        dialog.Close();
        Assert.True(TestComposition.RunUntil(() => !hotkeys.IsMessageDialogOpen, TimeSpan.FromSeconds(5)));
        if (main.IsVisible)
            main.Close();
    }

    // An update that is not launched or does not close the application leaves the dialog as it
    // was before Update was clicked; a failure is reported in the dialog, as before.
    [AvaloniaTheory]
    [InlineData("cancelled")]
    [InlineData("failed")]
    public void UpdateTheDialogOffersAgainWhenTheUpdateDoesNotCloseTheApplication(string outcome)
    {
        var (main, dialogs) = Create();
        main.Show();
        var installs = 0;
        _ = dialogs.ShowUpdateAvailableAsync(FakeReleaseSource.Release("9.8.7"), _ =>
        {
            installs++;
            return outcome == "failed"
                ? Task.FromException<bool>(new InvalidOperationException("Could not start the update helper."))
                : Task.FromResult(false);
        });
        Dispatcher.UIThread.RunJobs();
        var dialog = OwnedDialog<UpdateAvailableWindow>(main);
        var update = dialog.FindControl<Button>("UpdateButton")!;
        var later = dialog.FindControl<Button>("LaterButton")!;
        var progress = dialog.FindControl<TextBlock>("ProgressText")!;

        update.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, installs);
        Assert.True(update.IsEnabled);
        Assert.True(later.IsEnabled);
        Assert.Equal(outcome == "failed", progress.IsVisible);
        if (outcome == "failed")
            Assert.Equal("Update failed. Your current installation was not changed.\nCould not start the update helper.", progress.Text);
        dialog.Close();
        main.Close();
    }

    [AvaloniaFact]
    public void UpdateDialogShowsTheRelease()
    {
        var (main, dialogs) = Create();
        main.Show();

        _ = dialogs.ShowUpdateAvailableAsync(FakeReleaseSource.Release("9.8.7"), _ => Task.FromResult(true));
        Dispatcher.UIThread.RunJobs();

        var dialog = OwnedDialog<UpdateAvailableWindow>(main);
        Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Text == "NvProfileSwitcher 9.8.7 is available");
        dialog.Close();
        main.Close();
    }

    [AvaloniaFact]
    public void AboutDialogIsShownOverTheMainWindow()
    {
        var (main, dialogs) = Create();
        main.Show();

        _ = dialogs.ShowAboutAsync();
        Dispatcher.UIThread.RunJobs();

        OwnedDialog<AboutWindow>(main).Close();
        main.Close();
    }
}
