using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using static NvProfileSwitcher.Core.Tests.FakeDisplayBackend;

namespace NvProfileSwitcher.UiTests;

// The persistence guarantees over the composed application: a profile is applied only once
// saved, the load notice is shown once, and changes left by a failed save get one more
// attempt when the window closes for good.
public sealed class PersistenceIntegrationTests
{
    private const string Dell = "DEL4321-ABC123";

    // Ctrl + Shift + G.
    private const int GameHotkey = 0x0347;

    private static readonly string Config = $$"""
        {
          "Windows Profiles":[{"DisplayName":"DELL U2720Q","MonitorId":"{{Dell}}","Brightness":100,"Contrast":100,"Gamma":1,"DigitalVibrance":50,"Hue":0}],
          "Application Profiles":[
            {"Id":"11111111-1111-1111-1111-111111111111","Name":"Game","Exe Paths":["game.exe"],"Enabled":true,"Hotkey":{{GameHotkey}},
             "Display Profiles":[{"DisplayName":"DELL U2720Q","MonitorId":"{{Dell}}","Brightness":105,"Contrast":100,"Gamma":1,"DigitalVibrance":70,"Hue":0}]}
          ]
        }
        """;

    private static TestComposition Create(string? config = null)
    {
        var app = new TestComposition(config ?? Config);
        app.Factory.Configure = backend => backend.Displays.Add(Display(Dell, "DELL U2720Q"));
        app.ShowActive();
        app.Hotkeys.Attach(42);
        return app;
    }

    // Saving fails while a directory takes the place of the temporary file.
    private static string BreakSaving(TestComposition app) =>
        Directory.CreateDirectory(app.ConfigPath + ".tmp").FullName;

    private static string Saved(TestComposition app) => File.ReadAllText(app.ConfigPath);

    private static ProfileListItem Game(TestComposition app) => app.Profiles.Profiles.Single(p => p.Id == Guid.Parse("11111111-1111-1111-1111-111111111111"));

    private static async Task EditGame(TestComposition app)
    {
        app.ProfilesPanel.SelectedProfile = Game(app);
        Dispatcher.UIThread.RunJobs();
        await Task.Yield();
        app.ProfileSettings.Name = "Changed";
        app.ProfileSettings.Brightness = 90;
    }

    // A failed save leaves the profile, the hotkeys, the preview and the session as they
    // were: the changes stay unsaved, and only the editor's own message is shown.
    [AvaloniaFact]
    public async Task AFailedProfileSaveChangesNothing()
    {
        using var app = Create();
        await EditGame(app);
        var before = Saved(app);
        var broken = BreakSaving(app);
        app.Registrar.Log.Clear();
        Assert.True(app.Preview.IsActive);

        await app.ProfileSettings.SaveCommand.ExecuteAsync(null);

        Assert.Equal([("Cannot save profile", "The profile could not be saved to the configuration file.")], app.Dialogs.Messages);
        Assert.Equal("Game", Game(app).Name);
        Assert.Equal(105, Game(app).DisplayProfiles[0].Brightness);
        Assert.Empty(app.Registrar.Log);
        Assert.True(app.Editor.IsDirty);
        Assert.True(app.ProfileSettings.CanCancel);
        Assert.Equal("Changed", app.ProfileSettings.Name);
        Assert.True(app.Preview.IsActive);
        Assert.Equal(before, Saved(app));

        // Once saving works again, the same draft is saved and applied.
        Directory.Delete(broken);
        await app.ProfileSettings.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Changed", Game(app).Name);
        Assert.Contains("\"Changed\"", Saved(app));
        Assert.False(app.Editor.IsDirty);
        Assert.False(app.Preview.IsActive);
        Assert.Single(app.Dialogs.Messages);
    }

    // The row's power button changes the profile while it is edited; saving keeps that
    // change, as the original copied the draft into the profile without Enabled.
    [AvaloniaFact]
    public async Task SavingKeepsThePowerStateChangedWhileEditing()
    {
        using var app = Create();
        await EditGame(app);
        app.ProfilesPanel.ToggleEnabledCommand.Execute(Game(app));

        await app.ProfileSettings.SaveCommand.ExecuteAsync(null);

        Assert.False(Game(app).Enabled);
        Assert.Contains("\"Enabled\": false", Saved(app));
        Assert.Contains("\"Changed\"", Saved(app));
    }

    // A failed save of the new profile keeps it unsaved: still in the list, still asked about
    // when closing, and not in config.json.
    [AvaloniaFact]
    public async Task AFailedSaveKeepsTheNewProfileUnsaved()
    {
        using var app = Create();
        await app.ProfilesPanel.AddCommand.ExecuteAsync(null);
        var added = app.Profiles.UnsavedNewProfile!;
        app.Editor.Session!.Draft.SetExecutables(["added.exe"]);
        app.ProfileSettings.Name = "Added";
        BreakSaving(app);

        await app.ProfileSettings.SaveCommand.ExecuteAsync(null);

        Assert.Equal([("Cannot save profile", "The profile could not be saved to the configuration file.")], app.Dialogs.Messages);
        Assert.Same(added, app.Profiles.UnsavedNewProfile);
        Assert.Contains(added, app.Profiles.Profiles);
        Assert.Equal("New Profile", added.Name);
        Assert.DoesNotContain("Added", Saved(app));

        app.Dialogs.DiscardAnswer = false;
        app.Window.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.True(app.Window.IsVisible);
        Assert.Equal([true], app.Dialogs.DiscardRequests);
    }

    // While the configuration is protected, every Save click is answered and nothing is
    // written; the automatic notice is not added to the load notice.
    [AvaloniaFact]
    public async Task SavingAProfileWhileProtectedWritesNothing()
    {
        using var app = new TestComposition(Config, configLockedWhileLoading: true);
        app.Factory.Configure = backend => backend.Displays.Add(Display(Dell, "DELL U2720Q"));
        app.ShowActive();
        var before = File.ReadAllBytes(app.ConfigPath);
        app.ProfileSettings.Brightness = 90;

        await app.ProfileSettings.SaveCommand.ExecuteAsync(null);
        await app.ProfileSettings.SaveCommand.ExecuteAsync(null);

        Assert.Contains("could not be read", app.Dialogs.Messages[0].Message);
        Assert.Equal(2, app.Dialogs.Messages.Count(m => m == ("Cannot save profile", "The profile could not be saved to the configuration file.")));
        Assert.Equal(3, app.Dialogs.Messages.Count);
        Assert.Equal(before, File.ReadAllBytes(app.ConfigPath));
        Assert.True(app.Editor.IsDirty);
    }

    // Each failed click on Save gets its message; the automatic notice is never added.
    [AvaloniaFact]
    public async Task EveryFailedProfileSaveIsReported()
    {
        using var app = Create();
        await EditGame(app);
        BreakSaving(app);

        await app.ProfileSettings.SaveCommand.ExecuteAsync(null);
        await app.ProfileSettings.SaveCommand.ExecuteAsync(null);

        Assert.Equal(2, app.Dialogs.Messages.Count);
        Assert.All(app.Dialogs.Messages, message =>
            Assert.Equal(("Cannot save profile", "The profile could not be saved to the configuration file."), message));
    }

    // The load notice is shown once the window opens, and not again when it is shown again.
    [AvaloniaFact]
    public void TheLoadNoticeIsShownOnceWhenTheWindowOpens()
    {
        using var app = new TestComposition("{ not json");
        Assert.Empty(app.Dialogs.Messages);

        app.ShowActive();
        app.Host.HideToTray();
        app.Host.ShowFromTray();
        Dispatcher.UIThread.RunJobs();

        Assert.Contains("is not valid", Assert.Single(app.Dialogs.Messages).Message);
    }

    // A change a failed save left only in memory is saved when the window closes.
    [AvaloniaFact]
    public void ClosingSavesTheChangesAFailedSaveLeft()
    {
        using var app = Create();
        var before = Saved(app);
        var broken = BreakSaving(app);
        app.ProfilesPanel.ToggleEnabledCommand.Execute(Game(app));
        Assert.False(Game(app).Enabled);
        Assert.Equal(before, Saved(app));
        Assert.Single(app.Dialogs.Messages);
        Directory.Delete(broken);

        app.Window.Close();

        Assert.False(app.Window.IsVisible);
        Assert.Contains("\"Enabled\": false", Saved(app));
    }

    // The last attempt runs only once the window really closes: "Keep editing" keeps it open
    // without saving, and discarding saves the applied changes without the draft.
    [AvaloniaFact]
    public async Task TheLastAttemptFollowsTheCloseGuard()
    {
        using var app = Create();
        var broken = BreakSaving(app);
        app.ProfilesPanel.ToggleEnabledCommand.Execute(Game(app));
        await EditGame(app);
        Directory.Delete(broken);
        var before = Saved(app);
        app.Dialogs.DiscardAnswer = false;

        app.Window.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.True(app.Window.IsVisible);
        Assert.Equal(before, Saved(app));

        app.Dialogs.DiscardAnswer = true;
        app.Window.Close();
        Assert.True(TestComposition.RunUntil(() => !app.Window.IsVisible, TimeSpan.FromSeconds(5)));

        var saved = Saved(app);
        Assert.Contains("\"Enabled\": false", saved);
        Assert.Contains("\"Game\"", saved);
        Assert.DoesNotContain("\"Changed\"", saved);
        Assert.Contains("\"Brightness\": 105", saved);
    }

    // Nothing was pending: closing writes nothing.
    [AvaloniaFact]
    public void ClosingWithoutPendingChangesWritesNothing()
    {
        using var app = Create();
        var lastWrite = File.GetLastWriteTimeUtc(app.ConfigPath);
        var before = Saved(app);

        app.Window.Close();

        Assert.Equal(before, Saved(app));
        Assert.Equal(lastWrite, File.GetLastWriteTimeUtc(app.ConfigPath));
    }
}
