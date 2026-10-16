using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NvProfileSwitcher.Core.Tests;
using NvProfileSwitcher.Models;
using static NvProfileSwitcher.Core.Tests.FakeDisplayBackend;

namespace NvProfileSwitcher.UiTests;

// What the profile list and editor actions leave on the displays, in the configuration and
// in the registered hotkeys, over the composed application: two displays, the Windows profile
// and two enabled application profiles with hotkeys, the window shown and active.
public sealed class ProfileBehaviorTests
{
    private const string Dell = "DEL4321-ABC123";
    private const string Lg = "GSM5B7F-305NTAB1C2D3";

    // Ctrl + Shift + G and Ctrl + Shift + N.
    private const int GameHotkey = 0x0347;
    private const int NotepadHotkey = 0x034E;

    private static string Values(string monitorId, int vibrance, double brightness) =>
        $$"""{"DisplayName":"{{monitorId}}","MonitorId":"{{monitorId}}","Brightness":{{brightness}},"Contrast":100,"Gamma":1,"DigitalVibrance":{{vibrance}},"Hue":0}""";

    private static string Profile(string id, string name, string exe, int hotkey, int vibrance, double brightness) =>
        $$"""{"Id":"{{id}}","Name":"{{name}}","Exe Paths":["{{exe}}"],"Enabled":true,"Hotkey":{{hotkey}},"Display Profiles":[{{Values(Dell, vibrance, brightness)}},{{Values(Lg, vibrance + 1, brightness)}}]}""";

    private static readonly string Config = $$"""
        {
          "Windows Profiles":[{{Values(Dell, 50, 100)}},{{Values(Lg, 51, 100)}}],
          "Application Profiles":[
            {{Profile("11111111-1111-1111-1111-111111111111", "Game", "game.exe", GameHotkey, 70, 110)}},
            {{Profile("22222222-2222-2222-2222-222222222222", "Notepad", "notepad.exe", NotepadHotkey, 60, 95)}}
          ]
        }
        """;

    private const string WindowsOnlyConfig = $$"""{"Windows Profiles":[],"Application Profiles":[]}""";

    private static TestComposition Create(string? config = null, Action<FakeDisplayBackend>? displays = null)
    {
        var app = new TestComposition(config ?? Config);
        app.Factory.Configure = backend =>
        {
            backend.Displays.Add(Display(Dell, "DELL U2720Q"));
            backend.Displays.Add(Display(Lg, "LG ULTRAGEAR", 2));
            displays?.Invoke(backend);
        };
        app.ShowActive();
        app.Hotkeys.Attach(42);
        return app;
    }

    private static ProfileListItem Named(TestComposition app, string name) => app.Profiles.Profiles.Single(p => p.Name == name);

    private static async Task Select(TestComposition app, string name)
    {
        app.ProfilesPanel.SelectedProfile = Named(app, name);
        Dispatcher.UIThread.RunJobs();
        await Task.Yield();
    }

    // The foreground application changes and automatic switching applies its profile.
    private static void Foreground(TestComposition app, string executable)
    {
        app.Foreground = executable;
        app.Monitor.RaiseChanged();
        Dispatcher.UIThread.RunJobs();
    }

    private static ColorApplication Shown(TestComposition app, string monitorId) =>
        app.Factory.Created[^1].Current(monitorId)!.Value;

    private static string BreakSaving(TestComposition app) =>
        Directory.CreateDirectory(app.ConfigPath + ".tmp").FullName;

    private static string Saved(TestComposition app) => File.ReadAllText(app.ConfigPath);

    private static ProfileListItem? OverrideProfile(TestComposition app) =>
        app.Switching.OverrideMode == OverrideMode.Profile
            ? app.Profiles.ApplicationProfiles.ElementAtOrDefault(app.Switching.OverrideProfileIndex)
            : null;

    [AvaloniaFact]
    public async Task SavingAnotherProfileRestoresTheActiveProfileColors()
    {
        using var app = Create();
        Foreground(app, "game.exe");
        await Select(app, "Windows");
        app.ProfileSettings.Brightness = 90;
        Assert.Equal(90, Shown(app, Dell).Brightness);

        await app.ProfileSettings.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Game", app.Switching.ActiveProfileName);
        Assert.Equal(110, Shown(app, Dell).Brightness);
        Assert.Equal(110, Shown(app, Lg).Brightness);
    }

    [AvaloniaFact]
    public async Task RemovingTheActiveProfileAppliesTheNextTarget()
    {
        using var app = Create();
        Foreground(app, "notepad.exe");
        await Select(app, "Notepad");

        await app.ProfilesPanel.RemoveCommand.ExecuteAsync(null);

        Assert.Equal("Windows", app.Switching.ActiveProfileName);
        Assert.Equal(50, Shown(app, Dell).Vibrance);
        Assert.Equal(51, Shown(app, Lg).Vibrance);
    }

    [AvaloniaFact]
    public async Task AFailedSaveAfterRemovingKeepsTheCurrentColors()
    {
        using var app = Create();
        Foreground(app, "notepad.exe");
        await Select(app, "Notepad");
        BreakSaving(app);

        await app.ProfilesPanel.RemoveCommand.ExecuteAsync(null);

        Assert.DoesNotContain(app.Profiles.Profiles, p => p.Name == "Notepad");
        Assert.Equal("Notepad", app.Switching.ActiveProfileName);
        Assert.Equal(60, Shown(app, Dell).Vibrance);
    }

    [AvaloniaFact]
    public void DisablingTheActiveProfileAppliesWindows()
    {
        using var app = Create();
        Foreground(app, "game.exe");

        app.ProfilesPanel.ToggleEnabledCommand.Execute(Named(app, "Game"));

        Assert.Equal("Windows", app.Switching.ActiveProfileName);
        Assert.Equal(50, Shown(app, Dell).Vibrance);
        Assert.Contains("\"Enabled\": false", Saved(app));
        Assert.False(app.PressHotkey(GameHotkey));
    }

    [AvaloniaFact]
    public void AFailedSaveAfterDisablingKeepsTheCurrentColors()
    {
        using var app = Create();
        Foreground(app, "game.exe");
        BreakSaving(app);

        app.ProfilesPanel.ToggleEnabledCommand.Execute(Named(app, "Game"));

        Assert.False(Named(app, "Game").Enabled);
        Assert.Equal("Game", app.Switching.ActiveProfileName);
        Assert.Equal(70, Shown(app, Dell).Vibrance);
    }

    [AvaloniaFact]
    public async Task TogglingTheUnsavedProfileReevaluatesWithoutSaving()
    {
        using var app = Create();
        Foreground(app, "game.exe");
        await app.ProfilesPanel.AddCommand.ExecuteAsync(null);
        var before = Saved(app);
        app.ActiveProfile.Invalidate();

        app.ProfilesPanel.ToggleEnabledCommand.Execute(app.Profiles.UnsavedNewProfile);

        Assert.Equal(before, Saved(app));
        Assert.Equal("Game", app.Switching.ActiveProfileName);
    }

    [AvaloniaFact]
    public async Task AProfileHotkeyFollowsItsProfileWhenMoved()
    {
        using var app = Create();

        await app.ProfilesPanel.MoveUpCommand.ExecuteAsync(Named(app, "Notepad"));

        Assert.True(app.PressHotkey(NotepadHotkey));
        Assert.Same(Named(app, "Notepad"), OverrideProfile(app));
        Assert.True(app.PressHotkey(GameHotkey));
        Assert.Same(Named(app, "Game"), OverrideProfile(app));
    }

    [AvaloniaFact]
    public async Task AProfileHotkeyFollowsItsProfileWhenAnEarlierOneIsRemoved()
    {
        using var app = Create();
        await Select(app, "Game");

        await app.ProfilesPanel.RemoveCommand.ExecuteAsync(null);

        Assert.False(app.PressHotkey(GameHotkey));
        Assert.True(app.PressHotkey(NotepadHotkey));
        Assert.Same(Named(app, "Notepad"), OverrideProfile(app));
    }

    [AvaloniaFact]
    public async Task AProfileHotkeyFollowsItsProfileWhenTheUnsavedProfileIsDiscarded()
    {
        using var app = Create();
        await app.ProfilesPanel.AddCommand.ExecuteAsync(null);
        var added = app.Profiles.UnsavedNewProfile!;
        await app.ProfilesPanel.MoveUpCommand.ExecuteAsync(added);
        await app.ProfilesPanel.MoveUpCommand.ExecuteAsync(added);

        await app.ProfileSettings.CancelCommand.ExecuteAsync(null);

        Assert.Equal(["Windows", "Game", "Notepad"], app.Profiles.Profiles.Select(p => p.Name));
        Assert.True(app.PressHotkey(NotepadHotkey));
        Assert.Same(Named(app, "Notepad"), OverrideProfile(app));
    }

    [AvaloniaFact]
    public async Task DecliningTheCancelKeepsTheChanges()
    {
        using var app = Create();
        await Select(app, "Game");
        app.ProfileSettings.Name = "Game 2";
        app.ProfileSettings.Brightness = 90;
        var applied = app.Factory.Created[^1].Applied.Count;
        app.Dialogs.DiscardAnswer = false;

        await app.ProfileSettings.CancelCommand.ExecuteAsync(null);

        Assert.Equal("Game 2", app.ProfileSettings.Name);
        Assert.Equal(90, app.ProfileSettings.Brightness);
        Assert.True(app.ProfileSettings.CanCancel);
        Assert.Equal(applied, app.Factory.Created[^1].Applied.Count);
    }

    [AvaloniaFact]
    public async Task CopySettingsWithoutOtherProfilesDoesNothing()
    {
        using var app = Create(WindowsOnlyConfig);

        await app.ProfileSettings.CopySettingsCommand.ExecuteAsync(null);

        Assert.Empty(app.Dialogs.CopyRequests);
        Assert.False(app.ProfileSettings.CanCancel);
    }

    [AvaloniaFact]
    public async Task CancellingTheCopyDialogChangesNothing()
    {
        using var app = Create();
        await Select(app, "Game");
        var applied = app.Factory.Created[^1].Applied.Count;
        app.Dialogs.CopyAnswer = null;

        await app.ProfileSettings.CopySettingsCommand.ExecuteAsync(null);

        Assert.Single(app.Dialogs.CopyRequests);
        Assert.False(app.ProfileSettings.CanCancel);
        Assert.False(app.Preview.IsActive);
        Assert.Equal(applied, app.Factory.Created[^1].Applied.Count);
    }

    // A display whose monitor identity cannot be read is left out: it has no values in a new
    // profile and the editor does not offer it (deliberate deviation: the original added values
    // with an empty monitor id, which no display could match again).
    [AvaloniaFact]
    public async Task AddLeavesOutADisplayWithoutAnIdentity()
    {
        using var app = Create(displays: backend => backend.Displays.Add(Display("", "Projector", 3)));

        await app.ProfilesPanel.AddCommand.ExecuteAsync(null);

        var added = app.Profiles.UnsavedNewProfile!.DisplayProfiles;
        Assert.DoesNotContain(added, v => v.DisplayName == "Projector" || string.IsNullOrWhiteSpace(v.MonitorId));
        Assert.Equal(50, added.Single(v => v.MonitorId == Dell).DigitalVibrance);
        Assert.DoesNotContain(app.ProfileSettings.Displays, d => d.Name == "Projector");
    }

    [AvaloniaFact]
    public async Task ChoosingAnExecutableActionWithoutAPathKeepsTheSelection()
    {
        using var app = Create();
        await Select(app, "Game");
        var browse = app.ProfileSettings.ExecutableItems.Single(i => i.Text == "+ Browse...");
        app.Dialogs.ExecutableAnswer = null;

        app.ProfileSettings.SelectedExecutableItem = browse;
        Dispatcher.UIThread.RunJobs();
        await Task.Yield();

        Assert.Equal(1, app.Dialogs.ExecutableRequests);
        Assert.Equal("game.exe", app.ProfileSettings.SelectedExecutableItem!.Text);
        Assert.Equal(["game.exe"], app.Editor.Session!.Draft.ExecutablePaths);
        Assert.False(app.ProfileSettings.CanCancel);
    }

    [AvaloniaFact]
    public async Task AddingADuplicateExecutableChangesNothing()
    {
        using var app = Create();
        await Select(app, "Game");
        var running = app.ProfileSettings.ExecutableItems.Single(i => i.Text == "+ Running apps...");
        app.Dialogs.RunningAppAnswer = "GAME.EXE";

        app.ProfileSettings.SelectedExecutableItem = running;
        Dispatcher.UIThread.RunJobs();
        await Task.Yield();

        Assert.Equal(["game.exe"], app.Editor.Session!.Draft.ExecutablePaths);
        Assert.Equal("game.exe", app.ProfileSettings.SelectedExecutableItem!.Text);
        Assert.False(app.ProfileSettings.CanCancel);
    }

    // A monitor connected while the app runs: the refresh adds its values to every profile.
    private static void ConnectMonitor(TestComposition app)
    {
        var backend = app.Factory.Created[^1];
        var enumerations = backend.EnumerationCount;
        backend.Displays.Add(Display("ACI27A1-NEW", "ASUS VG27A", 3));
        app.Topology.HandleMessage(0x007E, 0);
        Assert.True(TestComposition.RunUntil(() => backend.EnumerationCount > enumerations, TimeSpan.FromSeconds(5)));
    }

    // The new monitor's values go into the edited profile; a draft without changes starts over
    // with them, so nothing counts as unsaved and closing does not ask.
    [AvaloniaFact]
    public async Task ConnectingAMonitorKeepsACleanDraftClean()
    {
        using var app = Create();
        await Select(app, "Game");

        ConnectMonitor(app);

        Assert.Contains(Named(app, "Game").DisplayProfiles, v => v.MonitorId == "ACI27A1-NEW");
        Assert.False(app.Editor.IsDirty);
        Assert.False(app.ProfileSettings.CanCancel);
        Assert.Same(Named(app, "Game"), app.Editor.Session!.Source);
        Assert.Equal(Dell, app.Editor.SelectedDisplay!.MonitorId);
        app.Window.Close();
        Assert.Empty(app.Dialogs.DiscardRequests);
    }

    // Every kind of user change keeps the draft as it is when a monitor is connected.
    [AvaloniaTheory]
    [InlineData("name")]
    [InlineData("slider")]
    [InlineData("executable")]
    [InlineData("hotkey")]
    [InlineData("copy")]
    [InlineData("reset")]
    [InlineData("unsaved")]
    public async Task ConnectingAMonitorKeepsTheUserChanges(string change)
    {
        using var app = Create();
        await Select(app, "Game");
        switch (change)
        {
            case "name":
                app.ProfileSettings.Name = "Racing";
                break;
            case "slider":
                app.ProfileSettings.Brightness = 90;
                break;
            case "executable":
                app.Dialogs.RunningAppAnswer = "racing.exe";
                await app.ProfileSettings.RunningAppsCommand.ExecuteAsync(null);
                break;
            case "hotkey":
                app.Editor.SetDraftHotkey(0x0352);
                break;
            case "copy":
                app.Dialogs.CopyAnswer = 0;
                await app.ProfileSettings.CopySettingsCommand.ExecuteAsync(null);
                break;
            case "reset":
                app.ProfileSettings.ResetCommand.Execute(null);
                break;
            case "unsaved":
                await app.ProfilesPanel.AddCommand.ExecuteAsync(null);
                break;
        }

        var draft = app.Editor.Session!.Draft;
        var expected = ProfileEditingCore.CloneProfile(draft);
        Assert.True(app.Editor.IsDirty);

        ConnectMonitor(app);

        Assert.Same(draft, app.Editor.Session!.Draft);
        Assert.True(app.Editor.IsDirty);
        Assert.True(app.ProfileSettings.CanCancel);
        Assert.True(ProfileEditingCore.ProfilesEqual(expected, draft));
    }
}
