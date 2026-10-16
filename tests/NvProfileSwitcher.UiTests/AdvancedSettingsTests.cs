using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NvProfileSwitcher.Core.Tests;
using NvProfileSwitcher.Shell;
using NvProfileSwitcher.Views;
using static NvProfileSwitcher.Core.Tests.FakeDisplayBackend;

namespace NvProfileSwitcher.UiTests;

// The Advanced Settings dialog (approved v2), its owner and the Advanced... action of the
// Application Settings panel.
public sealed class AdvancedSettingsTests
{
    private const string Dell = "DEL4321-ABC123";
    private const string Old = "SAM0F00-OLD";

    private sealed class Actions
    {
        public List<string> Log { get; } = [];
        public TaskCompletionSource? PendingCheck { get; set; }
        public bool ImportResult { get; set; }
        public AdvancedSettingsResult State { get; set; }

        public AdvancedSettingsOptions Options(bool rememberGlobal = false, bool rememberOverride = false) => new(
            new AdvancedSettingsResult(rememberGlobal, rememberOverride),
            () => State,
            () => { Log.Add("check"); return PendingCheck?.Task ?? Task.CompletedTask; },
            () => { Log.Add("displays"); return Task.CompletedTask; },
            () => { Log.Add("import"); return Task.FromResult(ImportResult); },
            () => { Log.Add("export"); return Task.CompletedTask; });
    }

    private static Button Button(Window window, string content) =>
        window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, content));

    private static List<CheckBox> CheckBoxes(Window window) => window.GetVisualDescendants().OfType<CheckBox>().ToList();

    private static void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void TheDialogHasTheApprovedLayout()
    {
        var window = new AdvancedSettingsWindow(new Actions().Options());
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Advanced Settings", window.Title);
        Assert.Equal(390, window.Width);
        Assert.Equal(SizeToContent.Height, window.SizeToContent);
        Assert.False(window.CanResize);
        Assert.False(window.ShowInTaskbar);
        Assert.Equal(WindowStartupLocation.CenterOwner, window.WindowStartupLocation);

        var grid = Assert.IsType<Grid>(window.Content);
        Assert.Equal(new Avalonia.Thickness(20), grid.Margin);
        Assert.Equal(8, grid.RowSpacing);
        Assert.Equal([12d, 12d, 12d, 20d], grid.RowDefinitions.Where(r => r.Height.IsAbsolute).Select(r => r.Height.Value));
        var titles = grid.Children.OfType<TextBlock>().ToList();
        Assert.Equal(["Updates", "State", "Displays", "Backup"], titles.Select(t => t.Text));
        Assert.All(titles, title => Assert.Equal(17, title.FontSize));

        Assert.Equal(["Remember Global Switching state", "Remember Windows Override state"], CheckBoxes(window).Select(c => c.Content));
        Assert.All(CheckBoxes(window), checkBox => Assert.Equal(default, checkBox.Margin));
        Assert.Equal(["Check for updates", "Manage displays...", "Import...", "Export...", "Cancel", "Save"],
            window.GetVisualDescendants().OfType<Button>().Where(b => b.GetType() == typeof(Button)).Select(b => b.Content));
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task SaveReturnsTheCheckedStateAndCancelReturnsNothing(bool rememberGlobal, bool rememberOverride)
    {
        var owner = new Window();
        owner.Show();
        var window = new AdvancedSettingsWindow(new Actions().Options(rememberGlobal, rememberOverride));
        var shown = window.ShowDialog<AdvancedSettingsResult?>(owner);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([rememberGlobal, rememberOverride], CheckBoxes(window).Select(c => c.IsChecked == true));

        CheckBoxes(window)[0].IsChecked = !rememberGlobal;
        Click(Button(window, "Save"));

        Assert.Equal(new AdvancedSettingsResult(!rememberGlobal, rememberOverride), await shown);

        window = new AdvancedSettingsWindow(new Actions().Options(rememberGlobal, rememberOverride));
        shown = window.ShowDialog<AdvancedSettingsResult?>(owner);
        Dispatcher.UIThread.RunJobs();
        Click(Button(window, "Cancel"));
        Assert.Null(await shown);
        owner.Close();
    }

    [AvaloniaFact]
    public void TheActionButtonsRunTheirActions()
    {
        var actions = new Actions();
        var window = new AdvancedSettingsWindow(actions.Options());
        window.Show();

        foreach (var content in new[] { "Check for updates", "Manage displays...", "Import...", "Export..." })
            Click(Button(window, content));

        Assert.Equal(["check", "displays", "import", "export"], actions.Log);
        Assert.True(window.IsVisible);
        window.Close();
    }

    // As in the original, the manual check button is disabled until the check ends.
    [AvaloniaFact]
    public void CheckForUpdatesIsDisabledWhileChecking()
    {
        var actions = new Actions { PendingCheck = new TaskCompletionSource() };
        var window = new AdvancedSettingsWindow(actions.Options());
        window.Show();
        var check = Button(window, "Check for updates");

        Click(check);
        Assert.False(check.IsEnabled);

        actions.PendingCheck.SetResult();
        Assert.True(TestComposition.RunUntil(() => check.IsEnabled, TimeSpan.FromSeconds(5)));
        window.Close();
    }

    // A successful import shows the imported Remember settings, so a later Save keeps them;
    // otherwise the user's unsaved choices stay (D2).
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheRememberSettingsAreReloadedAfterAnImport(bool imported)
    {
        var actions = new Actions { ImportResult = imported, State = new AdvancedSettingsResult(true, true) };
        var window = new AdvancedSettingsWindow(actions.Options());
        window.Show();
        CheckBoxes(window)[1].IsChecked = true;

        Click(Button(window, "Import..."));

        Assert.Equal(imported ? [true, true] : [false, true], CheckBoxes(window).Select(c => c.IsChecked == true));
        window.Close();
    }

    // While Advanced is open it owns the dialogs opened from it (D4); afterwards the main
    // window owns them again.
    [AvaloniaFact]
    public void AdvancedOwnsTheDialogsOpenedFromIt()
    {
        var host = new MainWindowHost();
        var main = new Window();
        host.Attach(main);
        main.Show();
        var dialogs = new DialogService(host);
        var actions = new Actions();
        var options = actions.Options() with
        {
            ManageDisplays = () => dialogs.ShowManageDisplaysAsync([new SavedDisplayInfo(Old, "Old", false)], _ => { }),
            Export = () => dialogs.ShowMessageAsync("Export configuration", "The configuration could not be exported.")
        };

        var shown = dialogs.ShowAdvancedSettingsAsync(options);
        Dispatcher.UIThread.RunJobs();
        var advanced = Assert.IsType<AdvancedSettingsWindow>(Assert.Single(main.OwnedWindows));

        Click(Button(advanced, "Manage displays..."));
        Assert.IsType<ManageDisplaysWindow>(Assert.Single(advanced.OwnedWindows)).Close();
        Click(Button(advanced, "Export..."));
        Assert.IsType<CenteredDialogWindow>(Assert.Single(advanced.OwnedWindows)).Close();
        Dispatcher.UIThread.RunJobs();
        Click(Button(advanced, "Cancel"));
        Assert.True(TestComposition.RunUntil(() => shown.IsCompleted, TimeSpan.FromSeconds(5)));

        _ = dialogs.ShowAboutAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<AboutWindow>(Assert.Single(main.OwnedWindows)).Close();
        main.Close();
    }

    [AvaloniaFact]
    public void TheAdvancedButtonOpensTheDialog()
    {
        using var app = new TestComposition();
        var view = new ApplicationSettingsView { DataContext = app.ApplicationSettings };
        var window = new Window { Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var advanced = view.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Advanced..."));
        advanced.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Single(app.Dialogs.AdvancedRequests);
        window.Close();
    }

    // With Save, the Remember settings are applied and saved; with Cancel nothing changes.
    [AvaloniaFact]
    public async Task SaveAppliesAndSavesTheRememberSettings()
    {
        using var app = new TestComposition("""{"RememberOverride":true}""");

        await app.ApplicationSettings.AdvancedCommand.ExecuteAsync(null);
        Assert.Equal(new AdvancedSettingsResult(false, true), app.Dialogs.AdvancedRequests[0].Initial);
        Assert.Equal("""{"RememberOverride":true}""", File.ReadAllText(app.ConfigPath));

        app.Dialogs.AdvancedAnswer = new AdvancedSettingsResult(true, false);
        await app.ApplicationSettings.AdvancedCommand.ExecuteAsync(null);

        Assert.True(app.Settings.RememberGlobalSwitching);
        Assert.False(app.Settings.RememberOverride);
        var saved = File.ReadAllText(app.ConfigPath);
        Assert.Contains("\"RememberGlobalSwitching\": true", saved);
        Assert.Contains("\"RememberOverride\": false", saved);
        Assert.Equal(new AdvancedSettingsResult(true, false), app.Dialogs.AdvancedRequests[1].CurrentState());
    }

    // A save failure is ignored (D3).
    [AvaloniaFact]
    public async Task AFailedSaveKeepsTheChosenSettings()
    {
        using var app = new TestComposition();
        Directory.CreateDirectory(app.ConfigPath + ".tmp");
        app.Dialogs.AdvancedAnswer = new AdvancedSettingsResult(true, true);

        await app.ApplicationSettings.AdvancedCommand.ExecuteAsync(null);

        Assert.True(app.Settings.RememberGlobalSwitching);
        Assert.True(app.Settings.RememberOverride);
        Assert.False(File.Exists(app.ConfigPath));
    }

    // The dialog's immediate actions reach the services: the manual update check, the saved
    // displays, Import and Export.
    [AvaloniaFact]
    public async Task TheDialogActionsReachTheServices()
    {
        using var app = new TestComposition("""
            {"Windows Profiles":[
              {"Name":"Display 1","DisplayName":"DELL","MonitorId":"DEL4321-ABC123","Brightness":100,"Contrast":100,"Gamma":1,"DigitalVibrance":50,"Hue":0},
              {"Name":"Display 2","DisplayName":"Old","MonitorId":"SAM0F00-OLD","Brightness":100,"Contrast":100,"Gamma":1,"DigitalVibrance":50,"Hue":0}]}
            """);
        app.Factory.Configure = backend => backend.Displays.Add(Display(Dell, "DELL U2720Q"));
        app.ShowActive();
        app.Dialogs.ManageDisplaysActions = remove => remove(Old);
        app.Dialogs.AdvancedActions = async options =>
        {
            await options.CheckForUpdates();
            await options.ManageDisplays();
            await options.Import();
            await options.Export();
        };

        await app.ApplicationSettings.AdvancedCommand.ExecuteAsync(null);

        Assert.Equal(FakeReleaseSource.NewerVersion, Assert.Single(app.Dialogs.UpdatesShown).Version);
        Assert.Equal(1, app.Releases.InfoRequests);
        var listed = Assert.Single(app.Dialogs.ManageDisplaysRequests);
        Assert.Equal([(Dell, true), (Old, false)], listed.Select(d => (d.MonitorId, d.Connected)));
        Assert.Equal([Dell], app.Profiles.Windows!.DisplayProfiles.Select(v => v.MonitorId));
        Assert.DoesNotContain(Old, File.ReadAllText(app.ConfigPath));
        Assert.Equal((1, 1), (app.Dialogs.ImportRequests, app.Dialogs.ExportRequests));
    }

    // Removing a saved display keeps the removal when saving fails (D3).
    [AvaloniaFact]
    public void AFailedSaveAfterRemovingADisplayIsIgnored()
    {
        using var app = new TestComposition("""
            {"Windows Profiles":[
              {"Name":"Display 2","DisplayName":"Old","MonitorId":"SAM0F00-OLD","Brightness":100,"Contrast":100,"Gamma":1,"DigitalVibrance":50,"Hue":0}]}
            """);
        Directory.CreateDirectory(app.ConfigPath + ".tmp");

        app.SavedDisplays.Remove(Old);

        Assert.Empty(app.Profiles.Windows!.DisplayProfiles);
        Assert.Contains(Old, File.ReadAllText(app.ConfigPath));
    }
}
