using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NvProfileSwitcher.Models;
using static NvProfileSwitcher.Core.Tests.FakeDisplayBackend;

namespace NvProfileSwitcher.UiTests;

// Import and Export over the composed application: two displays, the Windows profile and
// two application profiles, with the window shown and active and the hotkeys attached.
public sealed class BackupIntegrationTests
{
    private const string Dell = "DEL4321-ABC123";
    private const string Lg = "GSM5B7F-305NTAB1C2D3";

    // Ctrl + Shift + G, Ctrl + Shift + A and Ctrl + Shift + F1.
    private const int GameHotkey = 0x0347;
    private const int AlphaHotkey = 0x0341;
    private const int ShowHideHotkey = 0x0370;

    private static string Values(string monitorId, string name, int vibrance) =>
        $$"""{"DisplayName":"{{name}}","MonitorId":"{{monitorId}}","Brightness":100,"Contrast":100,"Gamma":1,"DigitalVibrance":{{vibrance}},"Hue":0}""";

    private static string Displays(int vibrance) =>
        $"{Values(Dell, "DELL U2720Q", vibrance)},{Values(Lg, "LG ULTRAGEAR", vibrance + 1)}";

    private static string Profile(string id, string name, string exe, int hotkey, int vibrance) =>
        $$"""{"Id":"{{id}}","Name":"{{name}}","Exe Paths":["{{exe}}"],"Enabled":true,"Hotkey":{{hotkey}},"Display Profiles":[{{Displays(vibrance)}}]}""";

    private static readonly string Config = $$"""
        {
          "Windows Profiles":[{{Displays(50)}}],
          "Application Profiles":[
            {{Profile("11111111-1111-1111-1111-111111111111", "Game", "game.exe", GameHotkey, 70)}},
            {{Profile("22222222-2222-2222-2222-222222222222", "Notepad", "notepad.exe", 0, 60)}}
          ]
        }
        """;

    private static string WindowsValues(string name, string monitorId, string displayName, int vibrance) =>
        $$"""{"Name":"{{name}}","DisplayName":"{{displayName}}","MonitorId":"{{monitorId}}","Brightness":100,"Contrast":100,"Gamma":1,"DigitalVibrance":{{vibrance}},"Hue":0}""";

    // Complete, as imports are validated strictly. Every connected display is already in every
    // profile, so importing it writes nothing else.
    private static string Imported(bool paused = false) => $$"""
        {
          "Windows Profiles":[{{WindowsValues("Display 1", Dell, "DELL U2720Q", 40)}},{{WindowsValues("Display 2", Lg, "LG ULTRAGEAR", 41)}}],
          "Application Profiles":[
            {{Profile("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "Alpha", "alpha.exe", AlphaHotkey, 80)}},
            {{Profile("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "Beta", "beta.exe", 0, 90)}}
          ],
          "StartWithWindows":true,
          "StartMinimized":false,
          "MinimizeToTray":false,
          "AlwaysOnTop":true,
          "CheckForUpdates":false,
          "RememberGlobalSwitching":true,
          "RememberOverride":false,
          "GlobalSwitchingEnabled":{{(paused ? "false" : "true")}},
          "OverrideMode":"Automatic",
          "OverrideProfileId":null,
          "ShowHideHotkey":{{ShowHideHotkey}},
          "WindowsOverrideHotkey":0,
          "ResumeAutomaticHotkey":0,
          "GlobalSwitchingHotkey":0
        }
        """;

    private static TestComposition Create()
    {
        var app = new TestComposition(Config);
        app.Factory.Configure = backend =>
        {
            backend.Displays.Add(Display(Dell, "DELL U2720Q"));
            backend.Displays.Add(Display(Lg, "LG ULTRAGEAR", 2));
        };
        app.ShowActive();
        app.Hotkeys.Attach(42);
        app.Hotkeys.RegisterAll(showAggregateMessage: false);
        app.Factory.Created[^1].Applied.Clear();
        app.Registrar.Log.Clear();
        app.Registry.Log.Clear();
        return app;
    }

    private static List<string> Names(TestComposition app) => app.Profiles.Profiles.Select(p => p.Name).ToList();

    private static List<int> AppliedVibrance(TestComposition app) =>
        app.Factory.Created[^1].Applied.Select(a => a.Vibrance).ToList();

    private static async Task<bool> Import(TestComposition app, string content)
    {
        app.Dialogs.ImportFile = content;
        var imported = await app.Backup.ImportAsync();
        Dispatcher.UIThread.RunJobs();
        return imported;
    }

    [AvaloniaFact]
    public async Task ImportReplacesTheConfigurationAndEditsWindows()
    {
        using var app = Create();
        var json = Imported();

        Assert.True(await Import(app, json));

        Assert.Equal(json, File.ReadAllText(app.ConfigPath));
        Assert.Equal(["Windows", "Alpha", "Beta"], Names(app));
        Assert.Same(app.Profiles.Windows, app.ProfilesPanel.SelectedProfile);
        Assert.Same(app.Profiles.Windows, app.Editor.Session!.Source);
        Assert.Equal("40%", app.ProfileSettings.VibranceText);
        Assert.False(app.Editor.IsDirty);
        Assert.True(app.Window.Topmost);
        Assert.True(app.ApplicationSettings.AlwaysOnTop);
        Assert.True(app.ApplicationSettings.StartWithWindows);
        Assert.Equal([$"set NvProfileSwitcher=\"{TestComposition.ExecutablePath}\""], app.Registry.Log);
        Assert.Equal("CTRL + SHIFT + F1", app.HotkeysPanel.ShowHide.Text);
        Assert.False(app.Profiles.Profiles[1].CanMoveUp);
        Assert.True(app.Profiles.Profiles[1].CanMoveDown);
        Assert.Empty(app.Dialogs.Messages);
    }

    // The hotkeys are unregistered before the list changes and registered again for the new
    // profiles; the active profile is cleared after that, before switching is evaluated.
    [AvaloniaFact]
    public async Task ImportFollowsTheOriginalOrder()
    {
        using var app = Create();
        app.Foreground = "alpha.exe";
        var log = app.Registrar.Log;
        ((System.Collections.Specialized.INotifyCollectionChanged)app.Profiles.Profiles).CollectionChanged += (_, e) => log.Add($"list {e.Action}");
        app.Switching.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SwitchingState.ActiveProfileName)) log.Add($"active '{app.Switching.ActiveProfileName}'");
        };

        Assert.True(await Import(app, Imported()));

        var reset = log.IndexOf("list Reset");
        Assert.True(reset > 0);
        Assert.All(log.Take(reset), entry => Assert.StartsWith("unregister", entry));
        var registrations = log.Skip(reset + 1).TakeWhile(entry => !entry.StartsWith("active", StringComparison.Ordinal)).ToList();
        Assert.Contains("register 4000 4006 41", registrations);
        Assert.Contains("register 3001 4006 70", registrations);
        Assert.DoesNotContain(registrations, entry => entry.StartsWith("register 4001", StringComparison.Ordinal));
        Assert.Equal(["active ''", "active 'Alpha'"], log.Skip(reset + 1 + registrations.Count));
        Assert.Equal([80, 81], AppliedVibrance(app).TakeLast(2));
    }

    [AvaloniaFact]
    public async Task AnImportedPausedStateForcesWindows()
    {
        using var app = Create();
        app.Foreground = "alpha.exe";

        Assert.True(await Import(app, Imported(paused: true)));

        Assert.True(app.Switching.GlobalPaused);
        Assert.False(app.Monitor.Running);
        Assert.Equal(string.Empty, app.Switching.ActiveProfileName);
        Assert.Equal([40, 41], AppliedVibrance(app).TakeLast(2));
        Assert.Equal("OFF", app.ViewModel.StatusBar.GlobalSwitchingText);
        Assert.False(((Avalonia.Controls.NativeMenuItem)app.Tray.Menu.Items[1]).IsChecked);
    }

    [AvaloniaFact]
    public async Task AnInvalidFileChangesNothing()
    {
        using var app = Create();
        var before = File.ReadAllText(app.ConfigPath);

        Assert.False(await Import(app, """{"Windows Profiles":[]}"""));

        Assert.Equal([("Invalid configuration file", "The selected file is not a valid NvProfileSwitcher configuration.")], app.Dialogs.Messages);
        Assert.Equal(before, File.ReadAllText(app.ConfigPath));
        Assert.Equal(["Windows", "Game", "Notepad"], Names(app));
        Assert.Empty(app.Registrar.Log);
    }

    [AvaloniaFact]
    public async Task AnUnreadableFileIsReportedAsInvalid()
    {
        using var app = Create();
        app.Dialogs.ImportOpenFailure = new IOException("locked");

        Assert.False(await Import(app, Imported()));

        Assert.Equal("Invalid configuration file", Assert.Single(app.Dialogs.Messages).Title);
        Assert.Equal(["Windows", "Game", "Notepad"], Names(app));
    }

    [AvaloniaFact]
    public async Task CancellingThePickerChangesNothing()
    {
        using var app = Create();

        Assert.False(await app.Backup.ImportAsync());

        Assert.Equal(1, app.Dialogs.ImportRequests);
        Assert.Empty(app.Dialogs.Messages);
        Assert.Empty(app.Registrar.Log);
    }

    // With unsaved changes the discard is confirmed before anything changes; "Keep editing"
    // cancels the import (deliberate deviation, D1).
    [AvaloniaFact]
    public async Task KeepEditingCancelsTheImport()
    {
        using var app = Create();
        app.ProfilesPanel.SelectedProfile = app.Profiles.Profiles[1];
        Dispatcher.UIThread.RunJobs();
        app.ProfileSettings.Name = "Changed";
        app.Dialogs.DiscardAnswer = false;
        var before = File.ReadAllText(app.ConfigPath);

        Assert.False(await Import(app, Imported()));

        Assert.Equal([false], app.Dialogs.DiscardRequests);
        Assert.Equal(before, File.ReadAllText(app.ConfigPath));
        Assert.Equal(["Windows", "Game", "Notepad"], Names(app));
        Assert.Equal("Changed", app.Editor.Session!.Draft.Name);
        Assert.Empty(app.Registrar.Log);
    }

    [AvaloniaFact]
    public async Task DiscardingTheChangesImports()
    {
        using var app = Create();
        app.ProfilesPanel.SelectedProfile = app.Profiles.Profiles[1];
        Dispatcher.UIThread.RunJobs();
        app.ProfileSettings.Name = "Changed";

        Assert.True(await Import(app, Imported()));

        Assert.Equal([false], app.Dialogs.DiscardRequests);
        Assert.Equal(["Windows", "Alpha", "Beta"], Names(app));
        Assert.Same(app.Profiles.Windows, app.Editor.Session!.Source);
        Assert.False(app.Editor.IsDirty);
    }

    [AvaloniaFact]
    public async Task TheUnsavedNewProfileIsDiscardedBeforeImporting()
    {
        using var app = Create();
        await app.ProfilesPanel.AddCommand.ExecuteAsync(null);

        Assert.True(await Import(app, Imported()));

        Assert.Equal([true], app.Dialogs.DiscardRequests);
        Assert.Null(app.Profiles.UnsavedNewProfile);
        Assert.Equal(["Windows", "Alpha", "Beta"], Names(app));
    }

    // A failure after the hotkeys were unregistered registers them again and is reported.
    [AvaloniaFact]
    public async Task AFailedImportRegistersTheHotkeysAgain()
    {
        using var app = Create();
        File.Delete(app.ConfigPath);
        Directory.CreateDirectory(app.ConfigPath);

        Assert.False(await Import(app, Imported()));

        Assert.Equal([("Import configuration", "The configuration could not be imported.")], app.Dialogs.Messages);
        Assert.Equal(["Windows", "Game", "Notepad"], Names(app));
        Assert.StartsWith("unregister", app.Registrar.Log[0]);
        Assert.Contains("register 4000 4006 47", app.Registrar.Registered());
    }

    // A failed save after the configuration was written and applied (here, the display
    // sync of a display the imported profiles lack) is reported as a failed save, not as a
    // failed import: the import stays applied.
    [AvaloniaFact]
    public async Task AFailedSaveAfterImportingDoesNotFailTheImport()
    {
        using var app = Create();
        var json = Imported().Replace("," + WindowsValues("Display 2", Lg, "LG ULTRAGEAR", 41), "");
        Assert.NotEqual(Imported(), json);
        Directory.CreateDirectory(app.ConfigPath + ".tmp");

        Assert.True(await Import(app, json));

        var notice = Assert.Single(app.Dialogs.Messages);
        Assert.Contains("could not be saved", notice.Message);
        Assert.Equal(json, File.ReadAllText(app.ConfigPath));
        Assert.Equal(["Windows", "Alpha", "Beta"], Names(app));
        Assert.Equal(2, app.Profiles.Windows!.DisplayProfiles.Count);
    }

    // A configuration that could not be read is never exported in its place.
    [AvaloniaFact]
    public async Task NothingIsExportedWhileTheConfigurationIsProtected()
    {
        using var app = new TestComposition(Config, configLockedWhileLoading: true);
        app.ShowActive();
        var before = File.ReadAllText(app.ConfigPath);
        app.Dialogs.ExportFile = new MemoryStream();

        await app.Backup.ExportAsync();

        Assert.Equal(0, app.Dialogs.ExportRequests);
        Assert.Equal(("Export configuration", "The configuration could not be exported."), app.Dialogs.Messages[^1]);
        Assert.Equal(before, File.ReadAllText(app.ConfigPath));
        Assert.Equal(0, app.Dialogs.ExportFile.Length);
    }

    [AvaloniaFact]
    public async Task ExportSavesAndCopiesTheConfiguration()
    {
        using var app = Create();
        app.Profiles.Profiles[2].Enabled = false;
        app.Dialogs.ExportFile = new MemoryStream();

        await app.Backup.ExportAsync();

        var saved = File.ReadAllBytes(app.ConfigPath);
        Assert.Contains("\"Enabled\": false", File.ReadAllText(app.ConfigPath));
        Assert.Equal(saved, app.Dialogs.ExportFile.ToArray());
        Assert.Empty(app.Dialogs.Messages);
    }

    // As in the original, the configuration is saved before the file is chosen.
    [AvaloniaFact]
    public async Task CancellingTheExportStillSaves()
    {
        using var app = Create();
        app.Profiles.Profiles[2].Enabled = false;

        await app.Backup.ExportAsync();

        Assert.Equal(1, app.Dialogs.ExportRequests);
        Assert.Contains("\"Enabled\": false", File.ReadAllText(app.ConfigPath));
        Assert.Empty(app.Dialogs.Messages);
    }

    // The unsaved new profile is not written to the configuration, so it is not exported.
    [AvaloniaFact]
    public async Task TheUnsavedNewProfileIsNotExported()
    {
        using var app = Create();
        await app.ProfilesPanel.AddCommand.ExecuteAsync(null);
        app.Dialogs.ExportFile = new MemoryStream();

        await app.Backup.ExportAsync();

        Assert.DoesNotContain("New Profile", System.Text.Encoding.UTF8.GetString(app.Dialogs.ExportFile.ToArray()));
    }

    [AvaloniaFact]
    public async Task AFailedExportIsReported()
    {
        using var app = Create();
        File.Delete(app.ConfigPath);
        Directory.CreateDirectory(app.ConfigPath);
        app.Dialogs.ExportFile = new MemoryStream();

        await app.Backup.ExportAsync();

        Assert.Equal([("Export configuration", "The configuration could not be exported.")], app.Dialogs.Messages);
        Assert.Equal(0, app.Dialogs.ExportRequests);
    }
}
