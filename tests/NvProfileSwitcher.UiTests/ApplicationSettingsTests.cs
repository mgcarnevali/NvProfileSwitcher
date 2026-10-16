using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NvProfileSwitcher.Views;

namespace NvProfileSwitcher.UiTests;

// The Application Settings checkboxes of the original: loading never saves, each change
// saves at once and runs its action in the original order, and failures stay silent.
public sealed class ApplicationSettingsTests
{
    private const string AllEnabled =
        """{"StartWithWindows":true,"StartMinimized":true,"MinimizeToTray":true,"AlwaysOnTop":true,"CheckForUpdates":true}""";

    private static string Saved(TestComposition app) => File.ReadAllText(app.ConfigPath);

    // Saving fails while a directory takes the place of the temporary file.
    private static void BreakSaving(TestComposition app) => Directory.CreateDirectory(app.ConfigPath + ".tmp");

    [AvaloniaFact]
    public void LoadingShowsTheSettingsWithoutSaving()
    {
        using var app = new TestComposition(AllEnabled);

        Assert.True(app.ApplicationSettings.StartWithWindows);
        Assert.True(app.ApplicationSettings.StartMinimized);
        Assert.True(app.ApplicationSettings.MinimizeToTray);
        Assert.True(app.ApplicationSettings.AlwaysOnTop);
        Assert.True(app.ApplicationSettings.CheckForUpdates);
        Assert.Equal(AllEnabled, Saved(app));
    }

    [AvaloniaFact]
    public void StartupRewritesTheStartupEntryBeforeTheWindowAndAppliesTopmost()
    {
        using var app = new TestComposition(AllEnabled);

        Assert.Equal([$"set NvProfileSwitcher=\"{TestComposition.ExecutablePath}\" --minimized"], app.Registry.Log);
        Assert.True(app.Window.Topmost);
    }

    [AvaloniaFact]
    public void SettingsChangedOutsideTheViewModelAreShownWithoutSaving()
    {
        using var app = new TestComposition();
        var changed = new List<string?>();
        app.ApplicationSettings.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        app.Settings.AlwaysOnTop = true;
        app.Settings.MinimizeToTray = true;

        Assert.Equal(["AlwaysOnTop", "MinimizeToTray"], changed);
        Assert.False(File.Exists(app.ConfigPath));
        Assert.False(app.Window.Topmost);
    }

    [AvaloniaTheory]
    [InlineData("StartWithWindows")]
    [InlineData("StartMinimized")]
    [InlineData("MinimizeToTray")]
    [InlineData("AlwaysOnTop")]
    [InlineData("CheckForUpdates")]
    public void EachSettingSavesAtOnce(string setting)
    {
        using var app = new TestComposition();

        typeof(NvProfileSwitcher.ViewModels.ApplicationSettingsViewModel).GetProperty(setting)!
            .SetValue(app.ApplicationSettings, true);

        Assert.Contains($"\"{setting}\": true", Saved(app));
    }

    [AvaloniaFact]
    public void StartupSettingsSaveAndThenWriteTheStartupEntry()
    {
        using var app = new TestComposition();
        app.Registry.Log.Clear();

        app.ApplicationSettings.StartWithWindows = true;
        app.ApplicationSettings.StartMinimized = true;
        app.ApplicationSettings.StartWithWindows = false;

        Assert.Equal(
        [
            $"set NvProfileSwitcher=\"{TestComposition.ExecutablePath}\"",
            $"set NvProfileSwitcher=\"{TestComposition.ExecutablePath}\" --minimized",
            "delete NvProfileSwitcher",
        ], app.Registry.Log);
    }

    [AvaloniaFact]
    public void FailedSaveSkipsTheStartupEntry()
    {
        using var app = new TestComposition();
        app.Registry.Log.Clear();
        BreakSaving(app);

        app.ApplicationSettings.StartWithWindows = true;

        Assert.True(app.Settings.StartWithWindows);
        Assert.Empty(app.Registry.Log);
    }

    [AvaloniaFact]
    public void AlwaysOnTopAppliesTopmostEvenWhenSavingFails()
    {
        using var app = new TestComposition();
        BreakSaving(app);

        app.ApplicationSettings.AlwaysOnTop = true;
        Assert.True(app.Window.Topmost);

        app.ApplicationSettings.AlwaysOnTop = false;
        Assert.False(app.Window.Topmost);
    }

    [AvaloniaFact]
    public void DisablingMinimizeToTrayHidesTheIconOnlyWhileTheWindowIsVisible()
    {
        using var app = new TestComposition();
        app.ApplicationSettings.MinimizeToTray = true;
        app.Tray.SetIconVisible(true);

        app.ApplicationSettings.MinimizeToTray = false;
        Assert.True(app.Tray.IsIconVisible);

        app.Window.Show();
        app.ApplicationSettings.MinimizeToTray = true;
        app.Tray.SetIconVisible(true);
        app.ApplicationSettings.MinimizeToTray = false;
        Assert.False(app.Tray.IsIconVisible);
    }

    [AvaloniaFact]
    public void FailedSaveKeepsTheTrayIcon()
    {
        using var app = new TestComposition();
        app.ApplicationSettings.MinimizeToTray = true;
        app.Window.Show();
        app.Tray.SetIconVisible(true);
        BreakSaving(app);

        app.ApplicationSettings.MinimizeToTray = false;

        Assert.True(app.Tray.IsIconVisible);
    }

    [AvaloniaFact]
    public void CheckboxesAreBoundToTheSettings()
    {
        using var app = new TestComposition();
        var view = new ApplicationSettingsView { DataContext = app.ApplicationSettings };
        var window = new Window { Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var boxes = view.GetVisualDescendants().OfType<CheckBox>().ToList();
        Assert.Equal(
            ["Start with Windows", "Start minimized", "Minimize to tray", "Always on top", "Check for updates"],
            boxes.Select(box => box.Content));

        boxes[3].IsChecked = true;
        Assert.True(app.Settings.AlwaysOnTop);
        Assert.True(app.Window.Topmost);

        app.Settings.StartMinimized = true;
        Assert.True(boxes[1].IsChecked);
        Assert.True(boxes[4].IsEnabled);
        Assert.Null(ToolTip.GetTip(boxes[4]));
        window.Close();
    }

    // As in the original Advanced dialog, development builds keep the update check off and
    // disabled, with a tooltip and without ShowOnDisabled.
    [AvaloniaFact]
    public void DevelopmentBuildDisablesCheckForUpdates()
    {
        using var app = new TestComposition(AllEnabled, isDevelopmentBuild: true);
        var view = new ApplicationSettingsView { DataContext = app.ApplicationSettings };
        var window = new Window { Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var box = view.GetVisualDescendants().OfType<CheckBox>().Single(b => Equals(b.Content, "Check for updates"));

        Assert.False(box.IsEnabled);
        Assert.False(box.IsChecked);
        Assert.Equal("Update checks are disabled in development builds.", ToolTip.GetTip(box));
        Assert.False(ToolTip.GetShowOnDisabled(box));
        window.Close();
    }
}
