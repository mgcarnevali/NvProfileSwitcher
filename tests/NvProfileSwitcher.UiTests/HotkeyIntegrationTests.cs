using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NvProfileSwitcher.Models;

namespace NvProfileSwitcher.UiTests;

// Global hotkeys across the window lifecycle: WM_HOTKEY actions, the window events that end
// a capture and the release on close.
public sealed class HotkeyIntegrationTests
{
    private const uint WmHotkey = 0x0312;

    private static TestComposition Create(string? configJson = null)
    {
        var app = new TestComposition(configJson);
        app.Window.Show();
        app.Window.Activate();
        Dispatcher.UIThread.RunJobs();
        app.Hotkeys.Attach(42);
        return app;
    }

    [AvaloniaFact]
    public void ShowHideHotkeyHidesToTheTrayAndShowsAgain()
    {
        using var app = Create();

        Assert.True(app.Host.DispatchMessage(WmHotkey, 3001));
        Assert.False(app.Window.IsVisible);
        Assert.True(app.Tray.IsIconVisible);

        Assert.True(app.Host.DispatchMessage(WmHotkey, 3001));
        Assert.True(app.Window.IsVisible);
        Assert.False(app.Tray.IsIconVisible);
    }

    [AvaloniaFact]
    public void SwitchingHotkeysCallTheSwitchingActions()
    {
        using var app = Create("""{"Application Profiles":[{"Name":"Game","Enabled":true,"Hotkey":839}]}""");

        app.Host.DispatchMessage(WmHotkey, 3002);
        Assert.Equal(OverrideMode.Windows, app.Switching.OverrideMode);

        app.Host.DispatchMessage(WmHotkey, 3003);
        Assert.Equal(OverrideMode.Automatic, app.Switching.OverrideMode);

        Assert.True(app.PressHotkey(839));
        Assert.Equal(OverrideMode.Profile, app.Switching.OverrideMode);
        Assert.Equal(0, app.Switching.OverrideProfileIndex);

        app.Host.DispatchMessage(WmHotkey, 3004);
        Assert.True(app.Switching.GlobalPaused);
        app.Host.DispatchMessage(WmHotkey, 3004);
        Assert.False(app.Switching.GlobalPaused);
    }

    [AvaloniaFact]
    public void HidingToTheTrayEndsTheCapture()
    {
        using var app = Create();
        app.Capture.Begin(HotkeyField.ShowHide);

        app.Host.HideToTray();

        Assert.Null(app.Capture.CaptureTarget);
    }

    [AvaloniaFact]
    public void MinimizingEndsTheCaptureEvenWithAMessageOpen()
    {
        using var app = Create();
        app.Dialogs.OpenMessage = new TaskCompletionSource();
        app.Capture.Begin(HotkeyField.ShowHide);
        app.Capture.KeyPressed(HotkeyField.ShowHide, Avalonia.Input.Key.A, Avalonia.Input.KeyModifiers.None);
        Assert.True(app.Hotkeys.IsMessageDialogOpen);

        app.Window.WindowState = WindowState.Minimized;

        Assert.Null(app.Capture.CaptureTarget);
        app.Dialogs.OpenMessage.SetResult();
    }

    // Headless windows do not deactivate each other, so the platform deactivation callback
    // that Avalonia sets on the window implementation is invoked directly.
    private static void Deactivate(TestComposition app)
    {
        var impl = app.Window.PlatformImpl!;
        var callback = impl.GetType()
            .GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .Single(property => property.Name.EndsWith("Deactivated", StringComparison.Ordinal));
        ((Action)callback.GetValue(impl)!).Invoke();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void DeactivatingEndsTheCaptureUnlessAMessageIsOpen()
    {
        using var app = Create();
        app.Dialogs.OpenMessage = new TaskCompletionSource();
        app.Capture.Begin(HotkeyField.ShowHide);
        app.Capture.KeyPressed(HotkeyField.ShowHide, Avalonia.Input.Key.A, Avalonia.Input.KeyModifiers.None);

        Deactivate(app);
        Assert.Equal(HotkeyField.ShowHide, app.Capture.CaptureTarget);

        app.Dialogs.OpenMessage.SetResult();
        Assert.True(TestComposition.RunUntil(() => !app.Hotkeys.IsMessageDialogOpen, TimeSpan.FromSeconds(5)));
        Deactivate(app);
        Assert.Null(app.Capture.CaptureTarget);
    }

    // As in the original, the window deactivates before it closes; the hotkeys registered
    // again at that point are released when it has closed.
    [AvaloniaFact]
    public void ClosingUnregistersEveryHotkey()
    {
        var app = Create("""{"ShowHideHotkey":624}""");
        app.Registrar.Log.Clear();

        app.Window.Close();
        app.Dispose();

        Assert.Equal(["unregister 3001", "unregister 3005"], app.Registrar.Log.TakeLast(2));
        Assert.Empty(app.Registrar.Active);
    }
}
