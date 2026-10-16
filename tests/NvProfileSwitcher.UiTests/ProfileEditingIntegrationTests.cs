using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NvProfileSwitcher.Core.Tests;
using NvProfileSwitcher.Models;
using static NvProfileSwitcher.Core.Tests.FakeDisplayBackend;

namespace NvProfileSwitcher.UiTests;

// The profile list actions and the editor over the composed application: two displays,
// the Windows profile and three application profiles, with the window shown and active.
public sealed class ProfileEditingIntegrationTests
{
    private const uint WmHotkey = 0x0312;
    private const string Dell = "DEL4321-ABC123";
    private const string Lg = "GSM5B7F-305NTAB1C2D3";

    // Ctrl + Shift + G, Ctrl + Shift + N and Ctrl + Shift + F1.
    private const int GameHotkey = 0x0347;
    private const int NotepadHotkey = 0x034E;
    private const int FreeHotkey = 0x0370;

    private static string Values(string monitorId, int vibrance, double brightness) =>
        $$"""{"DisplayName":"{{monitorId}}","MonitorId":"{{monitorId}}","Brightness":{{brightness}},"Contrast":100,"Gamma":1,"DigitalVibrance":{{vibrance}},"Hue":0}""";

    private static string Profile(string id, string name, string exe, bool enabled, int hotkey, int vibrance) =>
        $$"""{"Id":"{{id}}","Name":"{{name}}","Exe Paths":["{{exe}}"],"Enabled":{{(enabled ? "true" : "false")}},"Hotkey":{{hotkey}},"Display Profiles":[{{Values(Dell, vibrance, 105)}},{{Values(Lg, vibrance + 1, 106)}}]}""";

    private static readonly string Config = $$"""
        {
          "Windows Profiles":[{{Values(Dell, 50, 100)}},{{Values(Lg, 51, 100)}}],
          "Application Profiles":[
            {{Profile("11111111-1111-1111-1111-111111111111", "Game", "game.exe", true, GameHotkey, 70)}},
            {{Profile("22222222-2222-2222-2222-222222222222", "Notepad", "notepad.exe", true, NotepadHotkey, 60)}},
            {{Profile("33333333-3333-3333-3333-333333333333", "Editor", "editor.exe", false, 0, 40)}}
          ],
          "RememberOverride":true
        }
        """;

    private static TestComposition Create(string? config = null)
    {
        var app = new TestComposition(config ?? Config);
        app.Factory.Configure = backend =>
        {
            backend.Displays.Add(Display(Dell, "DELL U2720Q"));
            backend.Displays.Add(Display(Lg, "LG ULTRAGEAR", 2));
        };
        app.ShowActive();
        app.Hotkeys.Attach(42);
        app.Factory.Created[^1].Applied.Clear();
        return app;
    }

    private static ProfileListItem Named(TestComposition app, string name) => app.Profiles.Profiles.Single(p => p.Name == name);

    private static async Task Select(TestComposition app, string name)
    {
        app.ProfilesPanel.SelectedProfile = Named(app, name);
        Dispatcher.UIThread.RunJobs();
        await Task.Yield();
    }

    private static List<string> Names(TestComposition app) => app.Profiles.Profiles.Select(p => p.Name).ToList();

    private static string SavedJson(TestComposition app) => File.Exists(app.ConfigPath) ? File.ReadAllText(app.ConfigPath) : string.Empty;

    [AvaloniaFact]
    public void StartupSelectsAndEditsTheWindowsProfile()
    {
        using var app = Create();

        Assert.Equal(["Windows", "Game", "Notepad", "Editor"], Names(app));
        Assert.Same(app.Profiles.Windows, app.ProfilesPanel.SelectedProfile);
        Assert.Same(app.Profiles.Windows, app.Editor.Session!.Source);
        Assert.Equal(Dell, app.ProfileSettings.SelectedDisplay!.MonitorId);
        Assert.False(app.ProfileSettings.IsApplicationProfile);
        Assert.False(app.ProfileSettings.CanCancel);
        Assert.Equal("50%", app.ProfileSettings.VibranceText);
    }

    [AvaloniaFact]
    public async Task SelectingAProfileLoadsTheEditor()
    {
        using var app = Create();

        await Select(app, "Game");

        Assert.True(app.ProfileSettings.IsApplicationProfile);
        Assert.Equal("Game", app.ProfileSettings.Name);
        Assert.Equal(["game.exe", "+ Running apps...", "+ Browse..."], app.ProfileSettings.ExecutableItems.Select(i => i.Text));
        Assert.Equal("game.exe", app.ProfileSettings.SelectedExecutableItem!.Text);
        Assert.Equal("CTRL + SHIFT + G", app.ProfileSettings.ProfileHotkey.Text);
        Assert.Equal((105.0, 70.0), (app.ProfileSettings.Brightness, app.ProfileSettings.Vibrance));
    }

    [AvaloniaFact]
    public async Task KeepEditingReturnsTheListToTheEditedProfile()
    {
        using var app = Create();
        await Select(app, "Game");
        app.ProfileSettings.Name = "Game 2";
        app.Dialogs.DiscardAnswer = false;

        await Select(app, "Notepad");

        Assert.Equal([false], app.Dialogs.DiscardRequests);
        Assert.Same(Named(app, "Game"), app.ProfilesPanel.SelectedProfile);
        Assert.Equal("Game 2", app.ProfileSettings.Name);
        Assert.True(app.ProfileSettings.CanCancel);
    }

    [AvaloniaFact]
    public async Task AddCreatesAnUnsavedProfileWithTheWindowsValues()
    {
        using var app = Create();
        var saved = SavedJson(app);

        await app.ProfilesPanel.AddCommand.ExecuteAsync(null);

        var added = app.Profiles.Profiles[^1];
        Assert.Equal(("New Profile", true), (added.Name, added.Enabled));
        Assert.Same(added, app.Profiles.UnsavedNewProfile);
        Assert.Same(added, app.ProfilesPanel.SelectedProfile);
        Assert.Equal([(Dell, 50), (Lg, 51)], added.DisplayProfiles.Select(v => (v.MonitorId, v.DigitalVibrance)));
        Assert.True(app.ProfileSettings.CanCancel);
        Assert.True(Named(app, "Editor").CanMoveDown);
        Assert.Equal(saved, SavedJson(app));
    }

    [AvaloniaFact]
    public async Task AddingAgainDiscardsTheUnsavedProfile()
    {
        using var app = Create();
        await app.ProfilesPanel.AddCommand.ExecuteAsync(null);
        var first = app.Profiles.UnsavedNewProfile;

        await app.ProfilesPanel.AddCommand.ExecuteAsync(null);

        Assert.Equal([true], app.Dialogs.DiscardRequests);
        Assert.Equal(5, app.Profiles.Profiles.Count);
        Assert.DoesNotContain(first, app.Profiles.Profiles);
    }

    [AvaloniaFact]
    public async Task SaveValidatesTheApplicationProfile()
    {
        using var app = Create();
        await app.ProfilesPanel.AddCommand.ExecuteAsync(null);

        app.ProfileSettings.Name = "  ";
        await app.ProfileSettings.SaveCommand.ExecuteAsync(null);
        app.ProfileSettings.Name = "Game 2";
        await app.ProfileSettings.SaveCommand.ExecuteAsync(null);

        Assert.Equal(
            [("Cannot save profile", "Profile name is required.\nExecutable is required."),
             ("Cannot save profile", "Executable is required.")],
            app.Dialogs.Messages);
        Assert.NotNull(app.Profiles.UnsavedNewProfile);
    }

    [AvaloniaFact]
    public async Task SaveKeepsTheProfileAndTheSelectedDisplay()
    {
        using var app = Create();
        await app.ProfilesPanel.AddCommand.ExecuteAsync(null);
        app.Dialogs.ExecutableAnswer = @"C:\Games\new.exe";
        await app.ProfileSettings.BrowseCommand.ExecuteAsync(null);
        app.ProfileSettings.Name = " Racing ";
        app.ProfileSettings.SelectedDisplay = app.ProfileSettings.Displays[1];
        app.ProfileSettings.Vibrance = 75;

        await app.ProfileSettings.SaveCommand.ExecuteAsync(null);

        var saved = app.Profiles.Profiles[^1];
        Assert.Null(app.Profiles.UnsavedNewProfile);
        Assert.Equal(("Racing", @"C:\Games\new.exe", 75), (saved.Name, saved.ExecutablePaths[0], saved.DisplayProfiles[1].DigitalVibrance));
        Assert.Contains("\"Racing\"", SavedJson(app));
        Assert.Equal(Lg, app.ProfileSettings.SelectedDisplay!.MonitorId);
        Assert.Equal("Racing", app.ProfileSettings.Name);
        Assert.False(app.ProfileSettings.CanCancel);
        Assert.False(app.Preview.IsActive);
    }

    [AvaloniaFact]
    public async Task CancelDiscardsTheChangesAndReappliesTheRealColors()
    {
        using var app = Create();
        await Select(app, "Game");
        app.ProfileSettings.Brightness = 90;
        var backend = app.Factory.Created[^1];
        backend.Applied.Clear();

        await app.ProfileSettings.CancelCommand.ExecuteAsync(null);

        Assert.Equal([false], app.Dialogs.DiscardRequests);
        Assert.Equal(105, app.ProfileSettings.Brightness);
        Assert.False(app.ProfileSettings.CanCancel);
        Assert.Equal(105, DisplayCore.FindValues(Named(app, "Game"), Dell)!.Brightness);
        // Only the previewed display is given back to the real colors, once (deliberate
        // deviation: the original wrote the real colors to every display twice).
        Assert.Equal([new ColorApplication(Dell, 50, 100, 100, 1, 0)], backend.Applied);
    }

    [AvaloniaFact]
    public async Task CancellingTheUnsavedProfileRemovesIt()
    {
        using var app = Create();
        await app.ProfilesPanel.AddCommand.ExecuteAsync(null);

        await app.ProfileSettings.CancelCommand.ExecuteAsync(null);

        Assert.Equal(["Windows", "Game", "Notepad", "Editor"], Names(app));
        Assert.Same(Named(app, "Editor"), app.ProfilesPanel.SelectedProfile);
        Assert.Same(Named(app, "Editor"), app.Editor.Session!.Source);
    }

    // Removing the unsaved profile from the end updates ▲▼ (deliberate deviation: the
    // original left the new last profile able to move down).
    [AvaloniaFact]
    public async Task DiscardingTheUnsavedProfileUpdatesTheMoveState()
    {
        using var app = Create();
        await app.ProfilesPanel.AddCommand.ExecuteAsync(null);
        Assert.True(Named(app, "Editor").CanMoveDown);

        await app.ProfileSettings.CancelCommand.ExecuteAsync(null);

        Assert.False(Named(app, "Editor").CanMoveDown);
        Assert.True(Named(app, "Editor").CanMoveUp);
    }

    // Saves while the unsaved profile exists leave it out (deliberate deviation: the original
    // wrote it), so discarding it on close leaves nothing behind.
    [AvaloniaFact]
    public async Task OtherSavesDoNotWriteTheUnsavedProfile()
    {
        using var app = Create();
        await app.ProfilesPanel.AddCommand.ExecuteAsync(null);

        app.ProfilesPanel.ToggleEnabledCommand.Execute(Named(app, "Notepad"));
        Assert.Contains("\"Enabled\": false", SavedJson(app));
        Assert.DoesNotContain("New Profile", SavedJson(app));

        app.Window.Close();
        Assert.True(TestComposition.RunUntil(() => !app.Window.IsVisible, TimeSpan.FromSeconds(5)));
        Assert.DoesNotContain("New Profile", SavedJson(app));
    }

    // With the unsaved profile moved between two profiles and an override on the later one,
    // the saved override still names that profile.
    [AvaloniaFact]
    public async Task TheSavedOverrideIgnoresTheUnsavedProfileInTheMiddle()
    {
        using var app = Create();
        await app.ProfilesPanel.AddCommand.ExecuteAsync(null);
        var added = app.Profiles.UnsavedNewProfile!;
        await app.ProfilesPanel.MoveUpCommand.ExecuteAsync(added);
        await app.ProfilesPanel.MoveUpCommand.ExecuteAsync(added);
        Assert.Equal(["Windows", "Game", "New Profile", "Notepad", "Editor"], Names(app));

        app.Host.DispatchMessage(WmHotkey, 4002);
        Assert.Equal((OverrideMode.Profile, 2), (app.Switching.OverrideMode, app.Switching.OverrideProfileIndex));

        var saved = SavedJson(app);
        Assert.Contains("\"OverrideProfileId\": \"22222222-2222-2222-2222-222222222222\"", saved);
        Assert.DoesNotContain("New Profile", saved);
        Assert.True(saved.IndexOf("\"Game\"", StringComparison.Ordinal) < saved.IndexOf("\"Notepad\"", StringComparison.Ordinal));
    }

    // The unsaved profile moved up between Game and Notepad, with the Notepad override active.
    private static async Task<TestComposition> WithUnsavedProfileAboveNotepad()
    {
        var app = Create();
        await app.ProfilesPanel.AddCommand.ExecuteAsync(null);
        var added = app.Profiles.UnsavedNewProfile!;
        await app.ProfilesPanel.MoveUpCommand.ExecuteAsync(added);
        await app.ProfilesPanel.MoveUpCommand.ExecuteAsync(added);
        Assert.True(app.PressHotkey(NotepadHotkey));
        Assert.Equal((OverrideMode.Profile, 2), (app.Switching.OverrideMode, app.Switching.OverrideProfileIndex));
        return app;
    }

    private static async Task Discard(TestComposition app, string path)
    {
        switch (path)
        {
            case "select":
                await Select(app, "Editor");
                break;
            case "add":
                await app.ProfilesPanel.AddCommand.ExecuteAsync(null);
                break;
            case "cancel":
                await app.ProfileSettings.CancelCommand.ExecuteAsync(null);
                break;
            case "close":
                app.Window.Close();
                Assert.True(TestComposition.RunUntil(() => !app.Window.IsVisible, TimeSpan.FromSeconds(5)));
                break;
        }
    }

    // Discarding the moved unsaved profile keeps the hotkeys and the override on their
    // profiles (deliberate deviation: the original left them on the old positions).
    [AvaloniaTheory]
    [InlineData("select")]
    [InlineData("add")]
    [InlineData("cancel")]
    [InlineData("close")]
    public async Task DiscardingTheMovedUnsavedProfileKeepsHotkeysAndOverrideOnTheirProfiles(string path)
    {
        using var app = await WithUnsavedProfileAboveNotepad();

        await Discard(app, path);

        Assert.Equal([true], app.Dialogs.DiscardRequests);
        Assert.Equal(1, app.Profiles.ApplicationProfiles.ToList().IndexOf(Named(app, "Notepad")));
        Assert.Equal((OverrideMode.Profile, 1), (app.Switching.OverrideMode, app.Switching.OverrideProfileIndex));
        Assert.DoesNotContain(app.Dialogs.Messages, m => m.Message.StartsWith("One or more", StringComparison.Ordinal));
        if (path == "close")
            return;

        // Notepad's hotkey still toggles its own override, and the next save names Notepad.
        app.ProfilesPanel.ToggleEnabledCommand.Execute(Named(app, "Editor"));
        Assert.Contains("\"OverrideProfileId\": \"22222222-2222-2222-2222-222222222222\"", SavedJson(app));
        Assert.True(app.PressHotkey(NotepadHotkey));
        Assert.Equal(OverrideMode.Automatic, app.Switching.OverrideMode);
    }

    // The unsaved profile has no registered hotkey, so discarding it registers nothing again:
    // the other hotkeys stay registered on their profiles and no hotkey message is shown.
    [AvaloniaFact]
    public async Task DiscardingTheUnsavedProfileLeavesTheHotkeysRegistered()
    {
        using var app = await WithUnsavedProfileAboveNotepad();
        app.Registrar.Log.Clear();

        await app.ProfileSettings.CancelCommand.ExecuteAsync(null);

        Assert.DoesNotContain(app.Registrar.Log, entry => entry.StartsWith("register", StringComparison.Ordinal));
        Assert.DoesNotContain(app.Dialogs.Messages, m => m.Message.StartsWith("One or more", StringComparison.Ordinal));
        Assert.True(app.PressHotkey(GameHotkey));
        Assert.Equal((OverrideMode.Profile, 0), (app.Switching.OverrideMode, app.Switching.OverrideProfileIndex));
    }

    [AvaloniaFact]
    public async Task RemoveReleasesTheRemovedProfileHotkey()
    {
        using var app = Create();
        await Select(app, "Game");

        await app.ProfilesPanel.RemoveCommand.ExecuteAsync(null);

        Assert.False(app.PressHotkey(GameHotkey));
        Assert.True(app.PressHotkey(NotepadHotkey));
        Assert.Equal(["Game"], app.Dialogs.RemoveRequests);
        Assert.Equal(["Windows", "Notepad", "Editor"], Names(app));
        Assert.Same(Named(app, "Notepad"), app.ProfilesPanel.SelectedProfile);
        Assert.DoesNotContain("\"Game\"", SavedJson(app));
    }

    [AvaloniaFact]
    public async Task RemoveAdjustsTheProfileOverride()
    {
        using var app = Create();
        app.Host.DispatchMessage(WmHotkey, 4001);
        Assert.Equal((OverrideMode.Profile, 1), (app.Switching.OverrideMode, app.Switching.OverrideProfileIndex));
        await Select(app, "Game");

        await app.ProfilesPanel.RemoveCommand.ExecuteAsync(null);

        Assert.Equal((OverrideMode.Profile, 0), (app.Switching.OverrideMode, app.Switching.OverrideProfileIndex));
        Assert.Contains("\"OverrideProfileId\": \"22222222-2222-2222-2222-222222222222\"", SavedJson(app));
    }

    [AvaloniaFact]
    public async Task CancellingTheRemovalKeepsTheProfile()
    {
        using var app = Create();
        await Select(app, "Game");
        app.Dialogs.RemoveAnswer = false;

        await app.ProfilesPanel.RemoveCommand.ExecuteAsync(null);

        Assert.Equal(4, app.Profiles.Profiles.Count);
    }

    [AvaloniaFact]
    public async Task RemovingTheUnsavedProfileDoesNotSave()
    {
        using var app = Create();
        await app.ProfilesPanel.AddCommand.ExecuteAsync(null);
        var saved = SavedJson(app);

        await app.ProfilesPanel.RemoveCommand.ExecuteAsync(null);

        Assert.Null(app.Profiles.UnsavedNewProfile);
        Assert.Equal(4, app.Profiles.Profiles.Count);
        Assert.Same(Named(app, "Editor"), app.ProfilesPanel.SelectedProfile);
        Assert.Equal(saved, SavedJson(app));
    }

    [AvaloniaFact]
    public async Task MoveKeepsTheHotkeysOnTheirProfilesAndSelectsTheMovedProfile()
    {
        using var app = Create();
        Assert.True(app.PressHotkey(GameHotkey));

        await app.ProfilesPanel.MoveDownCommand.ExecuteAsync(Named(app, "Game"));

        Assert.Equal(["Windows", "Notepad", "Game", "Editor"], Names(app));
        Assert.Equal(1, app.Switching.OverrideProfileIndex);
        Assert.Same(Named(app, "Game"), app.ProfilesPanel.SelectedProfile);
        Assert.Same(Named(app, "Game"), app.Editor.Session!.Source);
        Assert.True(Named(app, "Game").CanMoveUp);
        Assert.True(SavedJson(app).IndexOf("\"Notepad\"", StringComparison.Ordinal) < SavedJson(app).IndexOf("\"Game\"", StringComparison.Ordinal));
        Assert.True(app.PressHotkey(NotepadHotkey));
        Assert.Equal((OverrideMode.Profile, 0), (app.Switching.OverrideMode, app.Switching.OverrideProfileIndex));
    }

    // Moving the edited profile keeps its draft and resumes its preview (deliberate deviation:
    // the original started the draft over and lost its unsaved changes).
    [AvaloniaFact]
    public async Task MovingTheEditedProfileKeepsItsDraftAndPreview()
    {
        using var app = Create();
        await Select(app, "Game");
        app.ProfileSettings.Name = "Changed";
        app.ProfileSettings.Brightness = 90;
        var backend = app.Factory.Created[^1];
        backend.Applied.Clear();

        await app.ProfilesPanel.MoveDownCommand.ExecuteAsync(Named(app, "Game"));

        Assert.Empty(app.Dialogs.DiscardRequests);
        Assert.Equal(["Windows", "Notepad", "Game", "Editor"], Names(app));
        Assert.Same(Named(app, "Game"), app.ProfilesPanel.SelectedProfile);
        Assert.Equal("Changed", app.ProfileSettings.Name);
        Assert.Equal(90, app.ProfileSettings.Brightness);
        Assert.True(app.ProfileSettings.CanCancel);
        // The preview goes on: its display is written once, with the preview, and never shows
        // the real colors in between (deliberate deviation: the original reapplied them first).
        Assert.True(app.Preview.IsActive);
        Assert.Equal((Dell, 90.0), (Assert.Single(backend.Applied).MonitorId, backend.Applied[0].Brightness));
        Assert.Contains("\"Game\"", SavedJson(app));
        Assert.DoesNotContain("\"Changed\"", SavedJson(app));
    }

    [AvaloniaFact]
    public async Task MovingTheUnsavedProfileKeepsItUnsaved()
    {
        using var app = Create();
        await app.ProfilesPanel.AddCommand.ExecuteAsync(null);
        app.ProfileSettings.Name = "Racing";
        var added = app.Profiles.UnsavedNewProfile!;

        await app.ProfilesPanel.MoveUpCommand.ExecuteAsync(added);

        Assert.Equal(["Windows", "Game", "Notepad", "New Profile", "Editor"], Names(app));
        Assert.Same(added, app.Profiles.UnsavedNewProfile);
        Assert.Equal("Racing", app.ProfileSettings.Name);
        Assert.DoesNotContain("New Profile", SavedJson(app));
    }

    [AvaloniaFact]
    public async Task MovingAnotherProfileAsksToDiscardTheChanges()
    {
        using var app = Create();
        await Select(app, "Game");
        app.ProfileSettings.Name = "Changed";
        app.Dialogs.DiscardAnswer = false;

        await app.ProfilesPanel.MoveUpCommand.ExecuteAsync(Named(app, "Editor"));

        Assert.Equal([false], app.Dialogs.DiscardRequests);
        Assert.Equal(["Windows", "Game", "Editor", "Notepad"], Names(app));
        Assert.Same(Named(app, "Game"), app.ProfilesPanel.SelectedProfile);
        Assert.Equal("Changed", app.ProfileSettings.Name);
    }

    [AvaloniaFact]
    public async Task TheFirstAndLastProfilesDoNotMovePastTheEnds()
    {
        using var app = Create();

        await app.ProfilesPanel.MoveUpCommand.ExecuteAsync(Named(app, "Game"));
        await app.ProfilesPanel.MoveDownCommand.ExecuteAsync(Named(app, "Editor"));
        await app.ProfilesPanel.MoveUpCommand.ExecuteAsync(app.Profiles.Windows);

        Assert.Equal(["Windows", "Game", "Notepad", "Editor"], Names(app));
    }

    [AvaloniaFact]
    public void ToggleRegistersTheHotkeysAgainAndSaves()
    {
        using var app = Create();
        app.Registrar.Log.Clear();

        app.ProfilesPanel.ToggleEnabledCommand.Execute(Named(app, "Game"));

        Assert.False(Named(app, "Game").Enabled);
        Assert.DoesNotContain("register 4000 4006 47", app.Registrar.Registered());
        Assert.Contains("register 4001 4006 4E", app.Registrar.Registered());
        Assert.Contains("\"Enabled\": false", SavedJson(app));
        Assert.Same(app.Profiles.Windows, app.ProfilesPanel.SelectedProfile);
    }

    [AvaloniaFact]
    public async Task CopySettingsCopiesTheColorsWithoutSaving()
    {
        using var app = Create();
        await Select(app, "Game");
        var saved = SavedJson(app);
        app.Dialogs.CopyAnswer = 1;

        await app.ProfileSettings.CopySettingsCommand.ExecuteAsync(null);

        Assert.Equal(["Windows", "Notepad", "Editor"], Assert.Single(app.Dialogs.CopyRequests));
        Assert.Equal(60, app.ProfileSettings.Vibrance);
        Assert.Equal(61, DisplayCore.FindValues(app.Editor.Session!.Draft, Lg)!.DigitalVibrance);
        Assert.True(app.ProfileSettings.CanCancel);
        Assert.True(app.Preview.IsActive);
        Assert.Equal(saved, SavedJson(app));
    }

    [AvaloniaFact]
    public async Task ResetShowsTheNvidiaDefaults()
    {
        using var app = Create();
        await Select(app, "Game");

        app.ProfileSettings.ResetCommand.Execute(null);

        Assert.Equal(("100", "100", "1.00", "50%", "0°"),
            (app.ProfileSettings.BrightnessText, app.ProfileSettings.ContrastText, app.ProfileSettings.GammaText,
             app.ProfileSettings.VibranceText, app.ProfileSettings.HueText));
        Assert.True(app.ProfileSettings.CanCancel);
        Assert.Equal(105, DisplayCore.FindValues(Named(app, "Game"), Dell)!.Brightness);
    }

    [AvaloniaFact]
    public async Task TheSelectorActionsAddExecutablesAndTheButtonsReplaceThem()
    {
        using var app = Create();
        await Select(app, "Game");
        var settings = app.ProfileSettings;

        app.Dialogs.RunningAppAnswer = @"C:\Games\second.exe";
        settings.SelectedExecutableItem = settings.ExecutableItems.Single(i => i.Action == ExecutableComboAction.AddRunningApp);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["game.exe", @"C:\Games\second.exe"], app.Editor.Session!.Draft.ExecutablePaths);
        Assert.Equal(@"C:\Games\second.exe", settings.SelectedExecutableItem!.Text);
        Assert.True(settings.ExecutableItems[0].CanRemove);

        app.Dialogs.ExecutableAnswer = @"C:\Games\THIRD.exe";
        await settings.BrowseCommand.ExecuteAsync(null);
        Assert.Equal(["game.exe", @"C:\Games\THIRD.exe"], app.Editor.Session.Draft.ExecutablePaths);

        app.Dialogs.ExecutableAnswer = "GAME.EXE";
        await settings.BrowseCommand.ExecuteAsync(null);
        Assert.Equal(["game.exe", @"C:\Games\THIRD.exe"], app.Editor.Session.Draft.ExecutablePaths);

        settings.RemoveExecutableCommand.Execute(settings.ExecutableItems[0]);
        Assert.Equal([@"C:\Games\THIRD.exe"], app.Editor.Session.Draft.ExecutablePaths);
        Assert.False(settings.ExecutableItems[0].CanRemove);
        Assert.True(settings.CanCancel);
        Assert.Equal(["game.exe"], Named(app, "Game").ExecutablePaths);
    }

    [AvaloniaFact]
    public async Task TheProfileHotkeyGoesToTheDraftWithoutSaving()
    {
        using var app = Create();
        await Select(app, "Game");
        var saved = SavedJson(app);

        app.Capture.Begin(HotkeyField.Profile);
        Assert.True(app.ProfileSettings.CanCancel);
        app.Capture.KeyPressed(HotkeyField.Profile, Key.F1, KeyModifiers.Control | KeyModifiers.Shift);
        Dispatcher.UIThread.RunJobs();

        Assert.Null(app.Capture.CaptureTarget);
        Assert.Equal(FreeHotkey, app.Editor.Session!.Draft.Hotkey);
        Assert.Equal(GameHotkey, Named(app, "Game").Hotkey);
        Assert.Equal("CTRL + SHIFT + F1", app.ProfileSettings.ProfileHotkey.Text);
        Assert.True(app.ProfileSettings.CanCancel);
        Assert.Equal(saved, SavedJson(app));
    }

    // The edited profile's own saved hotkey is not a conflict; another profile's is.
    [AvaloniaFact]
    public async Task TheProfileHotkeyConflictsWithOtherProfilesOnly()
    {
        using var app = Create();
        await Select(app, "Game");

        app.Capture.Begin(HotkeyField.Profile);
        app.Capture.KeyPressed(HotkeyField.Profile, Key.N, KeyModifiers.Control | KeyModifiers.Shift);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("That hotkey is already assigned to Notepad.\n\nChoose a different combination.", Assert.Single(app.Dialogs.Messages).Message);

        app.Capture.KeyPressed(HotkeyField.Profile, Key.Escape, KeyModifiers.None);
        app.Capture.Begin(HotkeyField.Profile);
        app.Capture.KeyPressed(HotkeyField.Profile, Key.G, KeyModifiers.Control | KeyModifiers.Shift);
        Dispatcher.UIThread.RunJobs();
        Assert.Single(app.Dialogs.Messages);
        Assert.Equal(GameHotkey, app.Editor.Session!.Draft.Hotkey);
    }

    [AvaloniaFact]
    public async Task CancelWithoutChangesEndsTheProfileHotkeyCapture()
    {
        using var app = Create();
        await Select(app, "Game");
        app.Capture.Begin(HotkeyField.Profile);

        await app.ProfileSettings.CancelCommand.ExecuteAsync(null);

        Assert.Null(app.Capture.CaptureTarget);
        Assert.Empty(app.Dialogs.DiscardRequests);
        Assert.False(app.ProfileSettings.CanCancel);
    }

    [AvaloniaFact]
    public async Task SavingTheProfileHotkeyRegistersIt()
    {
        using var app = Create();
        await Select(app, "Game");
        app.Capture.Begin(HotkeyField.Profile);
        app.Capture.KeyPressed(HotkeyField.Profile, Key.F1, KeyModifiers.Control | KeyModifiers.Shift);
        Dispatcher.UIThread.RunJobs();

        await app.ProfileSettings.SaveCommand.ExecuteAsync(null);

        Assert.Equal(FreeHotkey, Named(app, "Game").Hotkey);
        Assert.Contains("register 4000 4006 70", app.Registrar.Registered());
    }

    [AvaloniaFact]
    public async Task SlidersPreviewAndBlockSwitchingWhileTheWindowIsActive()
    {
        using var app = Create();
        await Select(app, "Game");
        var backend = app.Factory.Created[^1];
        backend.Applied.Clear();

        app.ProfileSettings.Brightness = 90.4;
        app.Foreground = "notepad.exe";
        app.Monitor.RaiseChanged();

        Assert.Equal("90", app.ProfileSettings.BrightnessText);
        var applied = Assert.Single(backend.Applied);
        Assert.Equal((Dell, 90.4, 70), (applied.MonitorId, applied.Brightness, applied.Vibrance));
    }

    // Deactivating suspends the preview; activating again resumes it.
    [AvaloniaFact]
    public async Task WindowActivitySuspendsAndResumesThePreview()
    {
        using var app = Create();
        await Select(app, "Game");
        app.ProfileSettings.Brightness = 90;
        var backend = app.Factory.Created[^1];
        backend.Applied.Clear();

        app.Deactivate();
        Assert.False(app.Preview.IsActive);
        // Only the previewed display is given back to the real colors.
        Assert.Equal([new ColorApplication(Dell, 50, 100, 100, 1, 0)], backend.Applied);

        app.Window.Activate();
        Dispatcher.UIThread.RunJobs();
        Assert.True(app.Preview.IsActive);
        Assert.Equal(90, backend.Applied[^1].Brightness);
    }

    // As in the original, choosing the display again after a topology change resumes the preview.
    [AvaloniaFact]
    public async Task ATopologyChangeResumesThePreview()
    {
        using var app = Create();
        await Select(app, "Game");
        app.ProfileSettings.Brightness = 90;
        var backend = app.Factory.Created[^1];
        backend.Applied.Clear();

        app.Topology.HandleMessage(0x007E, 0);
        Assert.True(TestComposition.RunUntil(() => backend.EnumerationCount == 2, TimeSpan.FromSeconds(5)));

        Assert.True(app.Preview.IsActive);
        Assert.Equal(90, backend.Current(Dell)!.Value.Brightness);
        Assert.Equal(90, app.ProfileSettings.Brightness);
    }

    // Disconnecting another display keeps the preview on the edited display without showing
    // its real colors in between (deliberate deviation: the original reapplied them first).
    [AvaloniaFact]
    public async Task ATopologyChangeNeverFlashesTheRealColorsOnThePreviewedDisplay()
    {
        using var app = Create();
        await Select(app, "Game");
        app.ProfileSettings.Vibrance = 0;
        var backend = app.Factory.Created[^1];
        backend.Applied.Clear();

        backend.Displays.RemoveAll(display => display.MonitorId == Lg);
        app.Topology.HandleMessage(0x007E, 0);
        Assert.True(TestComposition.RunUntil(() => backend.EnumerationCount == 2, TimeSpan.FromSeconds(5)));

        var dell = backend.Applied.Where(application => application.MonitorId == Dell).ToList();
        Assert.NotEmpty(dell);
        Assert.All(dell, application => Assert.Equal(0, application.Vibrance));
        Assert.True(app.Preview.IsActive);
        Assert.Equal(0, app.ProfileSettings.Vibrance);
        Assert.True(app.Editor.IsDirty);
    }

    // When the previewed display is disconnected, the preview resumes on the display the editor
    // selects instead, which is written once, with the preview.
    [AvaloniaFact]
    public async Task ATopologyChangeWithoutThePreviewedDisplayPreviewsTheRemainingDisplayOnce()
    {
        using var app = Create();
        await Select(app, "Game");
        app.ProfileSettings.Vibrance = 0;
        var backend = app.Factory.Created[^1];
        backend.Applied.Clear();

        backend.Displays.RemoveAll(display => display.MonitorId == Dell);
        app.Topology.HandleMessage(0x007E, 0);
        Assert.True(TestComposition.RunUntil(() => backend.EnumerationCount == 2, TimeSpan.FromSeconds(5)));

        Assert.Equal(Lg, app.ProfileSettings.SelectedDisplay!.MonitorId);
        Assert.Equal([71], backend.Applied.Select(application => application.Vibrance));
        Assert.True(app.Preview.IsActive);
    }

    // A preview the window is not active for ends with the topology change: the reevaluation
    // switches to the real colors, and no previewed value is left on the display.
    [AvaloniaFact]
    public async Task ATopologyChangeWhileTheWindowIsInactiveLeavesTheRealColors()
    {
        using var app = Create();
        await Select(app, "Game");
        app.Deactivate();
        app.ProfileSettings.Vibrance = 0;
        Assert.True(app.Preview.IsActive);
        var backend = app.Factory.Created[^1];
        backend.Applied.Clear();

        app.Topology.HandleMessage(0x007E, 0);
        Assert.True(TestComposition.RunUntil(() => backend.EnumerationCount == 2, TimeSpan.FromSeconds(5)));

        Assert.False(app.Preview.IsActive);
        Assert.Equal([50, 51], backend.Applied.Select(application => application.Vibrance));
        Assert.True(app.Editor.IsDirty);
    }

    // A failed refresh does not resume the preview, so the real colors are restored.
    [AvaloniaFact]
    public async Task AFailedTopologyRefreshRestoresTheRealColors()
    {
        using var app = Create();
        await Select(app, "Game");
        app.ProfileSettings.Vibrance = 0;
        var backend = app.Factory.Created[^1];
        backend.Applied.Clear();

        backend.EnumerationFailure = new InvalidOperationException("enumeration failed");
        app.Topology.HandleMessage(0x007E, 0);
        Assert.True(TestComposition.RunUntil(() => backend.EnumerationCount == 2, TimeSpan.FromSeconds(5)));
        Dispatcher.UIThread.RunJobs();

        Assert.False(app.Preview.IsActive);
        Assert.Equal(50, backend.Current(Dell)!.Value.Vibrance);
        Assert.Equal(0, app.ProfileSettings.Vibrance);
        Assert.True(app.Editor.IsDirty);
    }

    // A monitor without serial number moved to another connection while its profile has
    // unsaved changes: the saved values and the draft follow it, and the draft stays unsaved.
    [AvaloniaFact]
    public async Task AMonitorMovedToAnotherConnectionKeepsItsValuesAndTheDraft()
    {
        const string model = "GSM5B7F";
        var before = $"{model}@{Instance(2)}";
        var after = $"{model}@{Instance(3)}";
        var config = $$"""
            {
              "Windows Profiles":[{{Values(Dell, 50, 100)}},{{Values(before, 51, 100)}}],
              "Application Profiles":[{{Profile("11111111-1111-1111-1111-111111111111", "Game", "game.exe", true, GameHotkey, 70)
                  .Replace(Lg, before)}}]
            }
            """;
        using var app = new TestComposition(config);
        app.Factory.Configure = backend =>
        {
            backend.Displays.Add(Display(Dell, "DELL U2720Q"));
            backend.Displays.Add(Display(model, "LG ULTRAGEAR", 2));
        };
        app.ShowActive();
        await Select(app, "Game");
        app.ProfileSettings.SelectedDisplay = app.ProfileSettings.Displays.Single(d => d.MonitorId == before);
        app.ProfileSettings.Vibrance = 10;
        var backend = app.Factory.Created[^1];

        backend.Displays.RemoveAll(display => display.DisplayId == 2);
        backend.Displays.Add(Display(model, "LG ULTRAGEAR", 3));
        app.Topology.HandleMessage(0x007E, 0);
        Assert.True(TestComposition.RunUntil(() => backend.EnumerationCount == 2, TimeSpan.FromSeconds(5)));

        var game = Named(app, "Game");
        Assert.Equal(after, app.ProfileSettings.SelectedDisplay!.MonitorId);
        Assert.Equal(10, app.ProfileSettings.Vibrance);
        Assert.True(app.Editor.IsDirty);
        Assert.Equal(10, DisplayCore.FindValues(app.Editor.Session!.Draft, after)!.DigitalVibrance);
        Assert.Equal(71, DisplayCore.FindValues(game, after)!.DigitalVibrance);
        Assert.Equal(51, DisplayCore.FindValues(app.Profiles.Windows!, after)!.DigitalVibrance);
        Assert.DoesNotContain(before, SavedJson(app));
    }

    // A monitor connected while a profile has unsaved changes keeps its values when the profile
    // is saved (deliberate deviation: saving removed it, and the next refresh created it again
    // from the colors it showed).
    [AvaloniaFact]
    public async Task SavingKeepsTheValuesOfAMonitorConnectedWhileEditing()
    {
        const string samsung = "SAM0F12-H4ZN901234";
        using var app = Create();
        await Select(app, "Windows");
        app.ProfileSettings.Vibrance = 40;
        var backend = app.Factory.Created[^1];
        backend.Vibrance[samsung] = 62;
        backend.Displays.Add(Display(samsung, "SAMSUNG Odyssey", 3));
        app.Topology.HandleMessage(0x007E, 0);
        Assert.True(TestComposition.RunUntil(() => backend.EnumerationCount == 2, TimeSpan.FromSeconds(5)));

        await app.ProfileSettings.SaveCommand.ExecuteAsync(null);

        var windows = app.Profiles.Windows!;
        Assert.Equal(40, DisplayCore.FindValues(windows, Dell)!.DigitalVibrance);
        Assert.Equal(62, DisplayCore.FindValues(windows, samsung)!.DigitalVibrance);
        Assert.Contains(samsung, SavedJson(app));
    }

    // A connected monitor removed from Manage displays starts over from the NVIDIA defaults: the
    // colors it shows were written by this application, so they are not its own (deliberate
    // deviation: the original read them, so a preview or another profile became its Windows values).
    [AvaloniaFact]
    public async Task AConnectedMonitorRemovedFromTheSavedDisplaysStartsFromTheDefaults()
    {
        using var app = Create();
        await Select(app, "Game");
        app.ProfileSettings.Vibrance = 0;
        var backend = app.Factory.Created[^1];
        backend.Vibrance[Dell] = 0;
        await app.ProfileSettings.CancelCommand.ExecuteAsync(null);

        app.SavedDisplays.Remove(Dell);
        app.Topology.HandleMessage(0x007E, 0);
        Assert.True(TestComposition.RunUntil(() => backend.EnumerationCount == 2, TimeSpan.FromSeconds(5)));

        Assert.Equal(50, DisplayCore.FindValues(app.Profiles.Windows!, Dell)!.DigitalVibrance);
    }

    // The previewed monitor briefly without identity is left as it is; once Windows reports it
    // again, the refresh that runs once more gives it the colors it should show.
    [AvaloniaFact]
    public async Task APreviewedMonitorBrieflyWithoutIdentityGetsItsColorsBack()
    {
        using var app = Create();
        await Select(app, "Game");
        app.ProfileSettings.Vibrance = 0;
        var backend = app.Factory.Created[^1];

        backend.Displays[0] = Display("", "DELL U2720Q");
        app.Topology.HandleMessage(0x007E, 0);
        Assert.True(TestComposition.RunUntil(() => backend.EnumerationCount == 2, TimeSpan.FromSeconds(5)));
        Assert.Equal(Lg, app.ProfileSettings.SelectedDisplay!.MonitorId);

        backend.Displays[0] = Display(Dell, "DELL U2720Q");
        Assert.True(TestComposition.RunUntil(() => backend.EnumerationCount == 3, TimeSpan.FromSeconds(5)));

        Assert.Equal(50, backend.Current(Dell)!.Value.Vibrance);
        Assert.True(app.Editor.IsDirty);
    }

    [AvaloniaFact]
    public async Task ClosingWithUnsavedChangesAsksFirst()
    {
        using var app = Create();
        await Select(app, "Game");
        app.ProfileSettings.Name = "Changed";
        app.Dialogs.DiscardAnswer = false;

        app.Window.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.True(app.Window.IsVisible);
        Assert.Equal([false], app.Dialogs.DiscardRequests);

        app.Dialogs.DiscardAnswer = true;
        app.Window.Close();
        Assert.True(TestComposition.RunUntil(() => !app.Window.IsVisible, TimeSpan.FromSeconds(5)));
        Assert.Equal([false, false], app.Dialogs.DiscardRequests);
        Assert.Null(app.Editor.Session);
    }

    // A second close while the confirmation is open does not ask again: the open confirmation
    // decides, and the window closes once (deliberate deviation: the original asked twice).
    [AvaloniaFact]
    public async Task ASecondCloseWhileConfirmingDoesNotAskAgain()
    {
        using var app = Create();
        await Select(app, "Game");
        app.ProfileSettings.Name = "Changed";
        var pending = new TaskCompletionSource<bool>();
        app.Dialogs.PendingDiscard = pending;
        var closed = 0;
        app.Window.Closed += (_, _) => closed++;

        app.Window.Close();
        Dispatcher.UIThread.RunJobs();
        app.Window.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([false], app.Dialogs.DiscardRequests);
        Assert.True(app.Window.IsVisible);

        pending.SetResult(true);
        Assert.True(TestComposition.RunUntil(() => !app.Window.IsVisible, TimeSpan.FromSeconds(5)));
        Assert.Equal(1, closed);
        Assert.Null(app.Editor.Session);
    }

    // Another action that would discard the changes, such as Add, is turned down while the
    // close confirmation is open, and keeps the changes.
    [AvaloniaFact]
    public async Task OtherDiscardRequestsWaitForTheOpenConfirmation()
    {
        using var app = Create();
        await Select(app, "Game");
        app.ProfileSettings.Name = "Changed";
        var pending = new TaskCompletionSource<bool>();
        app.Dialogs.PendingDiscard = pending;
        app.Window.Close();
        Dispatcher.UIThread.RunJobs();

        var add = app.ProfilesPanel.AddCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(add.IsCompleted);
        Assert.Single(app.Dialogs.DiscardRequests);
        Assert.Null(app.Profiles.UnsavedNewProfile);
        Assert.Equal("Changed", app.ProfileSettings.Name);
        pending.SetResult(false);
        Dispatcher.UIThread.RunJobs();
        Assert.True(app.Window.IsVisible);
    }

    [AvaloniaFact]
    public void ClosingWithoutChangesClosesDirectly()
    {
        using var app = Create();

        app.Window.Close();

        Assert.False(app.Window.IsVisible);
        Assert.Empty(app.Dialogs.DiscardRequests);
    }

    [AvaloniaFact]
    public async Task ClosingWithTheUnsavedProfileAsksToDiscardIt()
    {
        using var app = Create();
        await app.ProfilesPanel.AddCommand.ExecuteAsync(null);

        app.Window.Close();
        Assert.True(TestComposition.RunUntil(() => !app.Window.IsVisible, TimeSpan.FromSeconds(5)));

        Assert.Equal([true], app.Dialogs.DiscardRequests);
        Assert.Equal(4, app.Profiles.Profiles.Count);
    }

    [AvaloniaFact]
    public async Task BadgesUseTheOriginalFormats()
    {
        var culture = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("es-AR");
            using var app = Create();
            await Select(app, "Game");

            app.ProfileSettings.Gamma = 1.25;
            app.ProfileSettings.Vibrance = 50.5;
            app.ProfileSettings.Hue = 12.6;

            Assert.Equal(("1.25", "50%", "13°"), (app.ProfileSettings.GammaText, app.ProfileSettings.VibranceText, app.ProfileSettings.HueText));
            var values = DisplayCore.FindValues(app.Editor.Session!.Draft, Dell)!;
            Assert.Equal((50, 13), (values.DigitalVibrance, values.Hue));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = culture;
        }
    }
}
