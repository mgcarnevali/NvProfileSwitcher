using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NvProfileSwitcher.Core.Tests;
using static NvProfileSwitcher.Core.Tests.FakeDisplayBackend;

namespace NvProfileSwitcher.UiTests;

// Every operation writes each display at most once, with what it shows afterwards: no display
// goes through an intermediate state, the live preview included. The Game profile is edited
// with a preview of vibrance 0 on the DELL display.
public sealed class ColorWriteTests
{
    private const string Dell = "DEL4321-ABC123";
    private const string Lg = "GSM5B7F-305NTAB1C2D3";

    private static string Values(string monitorId, int vibrance) =>
        $$"""{"DisplayName":"{{monitorId}}","MonitorId":"{{monitorId}}","Brightness":100,"Contrast":100,"Gamma":1,"DigitalVibrance":{{vibrance}},"Hue":0}""";

    private static readonly string Config = $$"""
        {
          "Windows Profiles":[{{Values(Dell, 50)}},{{Values(Lg, 51)}}],
          "Application Profiles":[
            {"Id":"11111111-1111-1111-1111-111111111111","Name":"Game","Exe Paths":["game.exe"],"Enabled":true,"Hotkey":0,
             "Display Profiles":[{{Values(Dell, 70)}},{{Values(Lg, 71)}}]}
          ]
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
        return app;
    }

    // Starts the application and previews vibrance 0 on the DELL display of the Game profile.
    private static async Task<TestComposition> Previewing()
    {
        var app = Create();
        app.ShowActive();
        app.ProfilesPanel.SelectedProfile = app.Profiles.Profiles.Single(p => p.Name == "Game");
        Dispatcher.UIThread.RunJobs();
        await Task.Yield();
        app.ProfileSettings.Vibrance = 0;
        Assert.True(app.Preview.IsActive);
        return app;
    }

    private static FakeDisplayBackend Backend(TestComposition app) => app.Factory.Created[^1];

    // The writes since the last call, with the vibrance each display was written with.
    private static Dictionary<string, int> WrittenOnce(TestComposition app)
    {
        var applied = Backend(app).Applied;
        foreach (var display in applied.GroupBy(a => a.MonitorId))
            Assert.True(display.Count() == 1, $"{display.Key} was written {display.Count()} times: {string.Join(", ", display.Select(a => a.Vibrance))}.");
        var written = applied.ToDictionary(a => a.MonitorId, a => a.Vibrance);
        applied.Clear();
        return written;
    }

    [AvaloniaFact]
    public void StartupWritesEachDisplayOnce()
    {
        using var app = Create();

        app.ShowActive();

        Assert.Equal(new Dictionary<string, int> { [Dell] = 50, [Lg] = 51 }, WrittenOnce(app));
    }

    [AvaloniaFact]
    public async Task ASwitchEndingThePreviewWritesEachDisplayOnce()
    {
        using var app = await Previewing();
        app.Deactivate();
        app.ProfileSettings.Vibrance = 5;
        Backend(app).Applied.Clear();

        app.Foreground = "game.exe";
        app.Monitor.RaiseChanged();

        Assert.False(app.Preview.IsActive);
        Assert.Equal(new Dictionary<string, int> { [Dell] = 70, [Lg] = 71 }, WrittenOnce(app));
    }

    // As in the original, the override ends the preview and gives the displays back to the real
    // colors (Game, for the foreground application); the draft keeps its unsaved changes, so
    // the override waits until the preview no longer owns the display.
    [AvaloniaFact]
    public async Task AnOverrideEndingThePreviewWritesEachDisplayOnce()
    {
        using var app = await Previewing();
        app.Foreground = "game.exe";
        Backend(app).Applied.Clear();

        app.AutomaticSwitching.ToggleWindowsOverride();

        Assert.Equal(new Dictionary<string, int> { [Dell] = 70, [Lg] = 71 }, WrittenOnce(app));
    }

    [AvaloniaFact]
    public async Task PausingAndResumingWithAPreviewWriteEachDisplayOnce()
    {
        using var app = await Previewing();
        Backend(app).Applied.Clear();

        app.AutomaticSwitching.SetGlobalEnabled(false);
        Assert.Equal(new Dictionary<string, int> { [Dell] = 50, [Lg] = 51 }, WrittenOnce(app));

        app.AutomaticSwitching.SetGlobalEnabled(true);
        Assert.Equal(new Dictionary<string, int> { [Dell] = 0 }, WrittenOnce(app));
    }

    [AvaloniaFact]
    public async Task SavingThePreviewWritesEachDisplayOnce()
    {
        using var app = await Previewing();
        Backend(app).Applied.Clear();

        await app.ProfileSettings.SaveCommand.ExecuteAsync(null);

        Assert.Equal(new Dictionary<string, int> { [Dell] = 50, [Lg] = 51 }, WrittenOnce(app));
    }

    [AvaloniaFact]
    public async Task CancellingThePreviewWritesOnlyItsDisplay()
    {
        using var app = await Previewing();
        Backend(app).Applied.Clear();

        await app.ProfileSettings.CancelCommand.ExecuteAsync(null);

        Assert.Equal(new Dictionary<string, int> { [Dell] = 50 }, WrittenOnce(app));
    }

    [AvaloniaFact]
    public async Task ATopologyChangeWritesEachDisplayOnceKeepingThePreview()
    {
        using var app = await Previewing();
        Backend(app).Applied.Clear();

        app.Topology.HandleMessage(0x007E, 0);
        Assert.True(TestComposition.RunUntil(() => Backend(app).EnumerationCount == 2, TimeSpan.FromSeconds(5)));

        Assert.True(app.Preview.IsActive);
        Assert.Equal(new Dictionary<string, int> { [Dell] = 0, [Lg] = 51 }, WrittenOnce(app));
    }

    [AvaloniaFact]
    public async Task RemovingThePreviewedProfileWritesEachDisplayOnce()
    {
        using var app = await Previewing();
        app.Dialogs.RemoveAnswer = true;
        Backend(app).Applied.Clear();

        await app.ProfilesPanel.RemoveCommand.ExecuteAsync(null);

        Assert.Equal(new Dictionary<string, int> { [Dell] = 50, [Lg] = 51 }, WrittenOnce(app));
    }

    [AvaloniaFact]
    public async Task ClosingWithAPreviewWritesEachDisplayOnce()
    {
        using var app = await Previewing();
        app.Dialogs.DiscardAnswer = true;
        Backend(app).Applied.Clear();

        app.Window.Close();
        Assert.True(TestComposition.RunUntil(() => !app.Window.IsVisible, TimeSpan.FromSeconds(5)));

        // Discarding the changes and closing are two operations; neither shows anything but Windows.
        Assert.All(Backend(app).Applied, a => Assert.Equal(a.MonitorId == Dell ? 50 : 51, a.Vibrance));
        Assert.Equal([Dell, Lg], Backend(app).Applied.Select(a => a.MonitorId).Distinct().Order());
    }
}
