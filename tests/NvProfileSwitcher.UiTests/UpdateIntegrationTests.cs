using Avalonia.Headless.XUnit;
using NvProfileSwitcher.Core.Tests;

namespace NvProfileSwitcher.UiTests;

// The automatic update check of the original Opened handler: started on every opening,
// not awaited, and also when the NVIDIA API fails.
public sealed class UpdateIntegrationTests
{
    private static TestComposition Create()
    {
        var app = new TestComposition();
        app.Settings.CheckForUpdates = true;
        return app;
    }

    private static void ShowAgain(TestComposition app)
    {
        app.Window.Hide();
        app.Window.Show();
    }

    [AvaloniaFact]
    public void EveryOpeningStartsAnAutomaticCheck()
    {
        using var app = Create();

        app.Window.Show();
        Assert.Equal(1, app.Releases.InfoRequests);
        Assert.Single(app.Dialogs.UpdatesShown);

        ShowAgain(app);
        Assert.Equal(2, app.Releases.InfoRequests);
        Assert.Equal(2, app.Dialogs.UpdatesShown.Count);
    }

    [AvaloniaFact]
    public void DisabledAutomaticCheckDoesNotQueryOnOpening()
    {
        using var app = Create();
        app.Settings.CheckForUpdates = false;

        app.Window.Show();

        Assert.Equal(0, app.Releases.InfoRequests);
    }

    [AvaloniaFact]
    public void CheckRunsWhenTheNvidiaApiFails()
    {
        using var app = Create();
        app.Factory.NextFailure = new DllNotFoundException("nvapi64.dll");

        app.Window.Show();

        Assert.False(app.Nvidia.IsAvailable);
        Assert.Equal(1, app.Releases.InfoRequests);
    }

    [AvaloniaFact]
    public void OpeningDoesNotWaitForTheCheck()
    {
        using var app = Create();
        app.Releases.Pending = new TaskCompletionSource<UpdateReleaseInfo?>();

        app.Window.Show();

        Assert.True(app.Window.IsVisible);
        Assert.Equal(1, app.Monitor.StartCount);
        Assert.Equal(1, app.Releases.InfoRequests);
        Assert.Empty(app.Dialogs.UpdatesShown);

        app.Releases.Pending.SetResult(app.Releases.Info);
        Assert.True(TestComposition.RunUntil(() => app.Dialogs.UpdatesShown.Count == 1, TimeSpan.FromSeconds(5)));
    }

    // As in the original, the checks are independent: an opening while a check is pending
    // starts another one, and each offers the update when it completes.
    [AvaloniaFact]
    public void OpeningWhileACheckIsPendingStartsAnotherCheck()
    {
        using var app = Create();
        app.Releases.Pending = new TaskCompletionSource<UpdateReleaseInfo?>();

        app.Window.Show();
        ShowAgain(app);
        Assert.Equal(2, app.Releases.InfoRequests);

        app.Releases.Pending.SetResult(app.Releases.Info);
        Assert.True(TestComposition.RunUntil(() => app.Dialogs.UpdatesShown.Count == 2, TimeSpan.FromSeconds(5)));
    }

    // With unsaved editor changes, installing asks to discard them once the update is ready:
    // keeping them cancels the update; discarding them launches it and closes without asking
    // again, as nothing is left to confirm.
    [AvaloniaFact]
    public async Task InstallingWithUnsavedChangesAsksBeforeLaunching()
    {
        using var app = Create();
        app.Factory.Configure = backend => backend.Displays.Add(FakeDisplayBackend.Display("DEL4321-ABC123", "DELL U2720Q"));
        app.ShowActive();
        app.ProfileSettings.Brightness = 90;
        Assert.True(app.Editor.IsDirty);
        var install = Assert.Single(app.Dialogs.UpdateInstalls);
        var progress = new Progress<string>();

        app.Dialogs.DiscardAnswer = false;
        Assert.False(await install(progress));
        Assert.Equal([$"prepare {FakeReleaseSource.NewerVersion}", $"discard root {FakeReleaseSource.NewerVersion}"], app.Installer.Log);
        Assert.Equal(0, app.ShutdownRequests);
        Assert.True(app.Editor.IsDirty);

        app.Dialogs.DiscardAnswer = true;
        Assert.True(await install(progress));
        Assert.Equal(1, app.ShutdownRequests);
        Assert.Equal($"launch root {FakeReleaseSource.NewerVersion}", app.Installer.Log[^1]);
        Assert.Equal(2, app.Dialogs.DiscardRequests.Count);
        Assert.False(app.Editor.IsDirty);
    }
}
