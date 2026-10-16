using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NvProfileSwitcher.Core.Tests;
using static NvProfileSwitcher.Core.Tests.FakeDisplayBackend;

namespace NvProfileSwitcher.UiTests;

// The recovery after a return from sleep or hibernation: WM_POWERBROADCAST with a resume
// notification initializes the NVIDIA API again, refreshes the displays and writes each display
// once with what it shows. The Game profile is edited with a preview of vibrance 1 on DELL.
public sealed class ResumeRecoveryTests
{
    private const uint WmPowerBroadcast = 0x0218;
    private const nint PbtApmSuspend = 0x0004;
    private const nint PbtApmResumeSuspend = 0x0007;
    private const nint PbtApmResumeAutomatic = 0x0012;
    private const nint PbtPowerSettingChange = 0x8013;
    private const uint WmDisplayChange = 0x007E;
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

    private static TestComposition Started()
    {
        var app = new TestComposition(Config);
        app.Factory.Configure = backend =>
        {
            backend.Displays.Add(Display(Dell, "DELL U2720Q"));
            backend.Displays.Add(Display(Lg, "LG ULTRAGEAR", 2));
        };
        app.ShowActive();
        return app;
    }

    private static async Task<TestComposition> Previewing()
    {
        var app = Started();
        app.ProfilesPanel.SelectedProfile = app.Profiles.Profiles.Single(p => p.Name == "Game");
        Dispatcher.UIThread.RunJobs();
        await Task.Yield();
        app.ProfileSettings.Vibrance = 1;
        Assert.True(app.Preview.IsActive);
        return app;
    }

    private static void Resume(TestComposition app, nint notification = PbtApmResumeAutomatic) =>
        app.Host.DispatchMessage(WmPowerBroadcast, notification);

    private static bool Recovered(TestComposition app) =>
        TestComposition.RunUntil(() => app.Factory.Created.Count == 2 && app.Factory.Created[1].EnumerationCount > 0, TimeSpan.FromSeconds(5));

    // The vibrance each display was written with on the new API instance; each display once.
    private static Dictionary<string, int> WrittenOnce(FakeDisplayBackend backend)
    {
        foreach (var display in backend.Applied.GroupBy(a => a.MonitorId))
            Assert.True(display.Count() == 1, $"{display.Key} was written {display.Count()} times.");
        return backend.Applied.ToDictionary(a => a.MonitorId, a => a.Vibrance);
    }

    [AvaloniaFact]
    public void ResumeInitializesTheApiAgainAndWritesEachDisplayOnce()
    {
        using var app = Started();
        var previous = app.Factory.Created[0];

        Resume(app);
        Assert.True(Recovered(app));

        var renewed = app.Factory.Created[1];
        Assert.Equal(1, previous.DisposeCount);
        Assert.Equal(1, renewed.EnumerationCount);
        Assert.Equal(new Dictionary<string, int> { [Dell] = 50, [Lg] = 51 }, WrittenOnce(renewed));
        Assert.True(app.Nvidia.IsAvailable);
    }

    // Both resume notifications and the display change that usually follows recover once.
    [AvaloniaFact]
    public void TheMessagesOfOneResumeRecoverOnce()
    {
        using var app = Started();

        Resume(app, PbtApmResumeSuspend);
        Resume(app, PbtApmResumeAutomatic);
        app.Host.DispatchMessage(WmDisplayChange, 0);
        Assert.True(Recovered(app));
        TestComposition.RunUntil(() => false, TimeSpan.FromMilliseconds(800));

        Assert.Equal(2, app.Factory.Created.Count);
        Assert.Equal(1, app.Factory.Created[1].EnumerationCount);
    }

    [AvaloniaFact]
    public async Task ResumeKeepsThePreviewOnItsDisplay()
    {
        using var app = await Previewing();

        Resume(app);
        Assert.True(Recovered(app));

        Assert.True(app.Preview.IsActive);
        Assert.Equal(new Dictionary<string, int> { [Dell] = 1, [Lg] = 51 }, WrittenOnce(app.Factory.Created[1]));
        Assert.Equal(1, app.ProfileSettings.Vibrance);
    }

    [AvaloniaFact]
    public async Task ResumeWhilePausedRestoresWindows()
    {
        using var app = await Previewing();
        app.AutomaticSwitching.SetGlobalEnabled(false);

        Resume(app);
        Assert.True(Recovered(app));

        Assert.Equal(new Dictionary<string, int> { [Dell] = 50, [Lg] = 51 }, WrittenOnce(app.Factory.Created[1]));
    }

    [AvaloniaFact]
    public void ResumeAppliesTheActiveProfile()
    {
        using var app = Started();
        app.Window.Hide();
        app.Foreground = "game.exe";
        app.Monitor.RaiseChanged();

        Resume(app);
        Assert.True(Recovered(app));

        Assert.Equal(new Dictionary<string, int> { [Dell] = 70, [Lg] = 71 }, WrittenOnce(app.Factory.Created[1]));
    }

    [AvaloniaFact]
    public void AFailedInitializationAtResumeReportsTheApiUnavailable()
    {
        using var app = Started();
        var current = app.Factory.Created[0];
        current.Applied.Clear();
        app.Factory.NextFailure = new InvalidOperationException("NvAPI_Initialize failed");

        Resume(app);
        Assert.True(TestComposition.RunUntil(() => !app.Nvidia.IsAvailable, TimeSpan.FromSeconds(5)));

        Assert.Single(app.Factory.Created);
        Assert.Equal(0, current.DisposeCount);
        Assert.Empty(current.Applied);
    }

    [AvaloniaFact]
    public void OtherPowerNotificationsDoNothing()
    {
        using var app = Started();
        var current = app.Factory.Created[0];
        current.Applied.Clear();

        Resume(app, PbtApmSuspend);
        Resume(app, PbtPowerSettingChange);
        TestComposition.RunUntil(() => false, TimeSpan.FromMilliseconds(800));

        Assert.Single(app.Factory.Created);
        Assert.Equal(1, current.EnumerationCount);
        Assert.Empty(current.Applied);
    }
}
