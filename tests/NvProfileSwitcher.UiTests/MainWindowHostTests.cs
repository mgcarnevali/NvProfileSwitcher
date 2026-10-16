using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NvProfileSwitcher.Shell;

namespace NvProfileSwitcher.UiTests;

public sealed class MainWindowHostTests
{
    private static (MainWindowHost Host, Window Window) ShowAttached()
    {
        var host = new MainWindowHost();
        var window = new Window();
        host.Attach(window);
        window.Show();
        return (host, window);
    }

    [AvaloniaFact]
    public void StartHiddenToTrayRequiresAnAttachedWindow() =>
        Assert.Throws<InvalidOperationException>(() => new MainWindowHost().StartHiddenToTray());

    [AvaloniaFact]
    public void StartedHiddenWindowHidesAfterEveryOpenedHandlerOnce()
    {
        var host = new MainWindowHost();
        var window = new Window();
        host.Attach(window);
        host.StartHiddenToTray();
        var events = new List<string>();
        host.Opened += () => events.Add($"opened-1 visible={window.IsVisible}");
        host.Opened += () => events.Add($"opened-2 visible={window.IsVisible}");
        host.HidingToTray += () => events.Add("hiding");
        host.TrayIconVisibilityRequested += visible => events.Add($"tray={visible}");

        window.Show();

        Assert.Equal(["opened-1 visible=True", "opened-2 visible=True", "hiding", "tray=True"], events);
        Assert.False(window.IsVisible);
        Assert.False(window.ShowInTaskbar);

        events.Clear();
        host.ShowFromTray();

        Assert.True(window.IsVisible);
        Assert.DoesNotContain("hiding", events);
        window.Close();
    }

    [AvaloniaFact]
    public void WindowNotStartedHiddenStaysVisibleAfterOpened()
    {
        var (_, window) = ShowAttached();
        Assert.True(window.IsVisible);
        Assert.True(window.ShowInTaskbar);
        window.Close();
    }

    [AvaloniaFact]
    public void CloseGuardRequiresBothDelegates()
    {
        var host = new MainWindowHost();
        Assert.Throws<ArgumentNullException>(() => host.SetCloseGuard(null!, () => Task.FromResult(true)));
        Assert.Throws<ArgumentNullException>(() => host.SetCloseGuard(() => true, null!));
    }

    [AvaloniaFact]
    public void CloseGuardIsConfiguredOnlyOnce()
    {
        var host = new MainWindowHost();
        host.SetCloseGuard(() => true, () => Task.FromResult(true));
        Assert.Throws<InvalidOperationException>(() => host.SetCloseGuard(() => true, () => Task.FromResult(true)));
    }

    [AvaloniaFact]
    public void WindowStaysOpenWhenConfirmationIsDeclined()
    {
        var (host, window) = ShowAttached();
        var confirmation = new TaskCompletionSource<bool>();
        var closing = 0;
        host.Closing += () => closing++;
        host.SetCloseGuard(() => true, () => confirmation.Task);

        window.Close();
        confirmation.SetResult(false);
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.IsVisible);
        Assert.Equal(0, closing);
        window.Close();
    }

    [AvaloniaFact]
    public void WindowClosesOnceAfterConfirmation()
    {
        var (host, window) = ShowAttached();
        var confirmation = new TaskCompletionSource<bool>();
        var asked = 0;
        var closing = 0;
        var closed = 0;
        host.Closing += () => closing++;
        host.Closed += () => closed++;
        host.SetCloseGuard(() => true, () =>
        {
            asked++;
            return confirmation.Task;
        });

        window.Close();
        Assert.True(window.IsVisible);
        confirmation.SetResult(true);
        Dispatcher.UIThread.RunJobs();

        Assert.False(window.IsVisible);
        Assert.Equal(1, asked);
        Assert.Equal(1, closing);
        Assert.Equal(1, closed);
    }

    [AvaloniaFact]
    public void WindowClosesWithoutAskingWhenNoConfirmationIsRequired()
    {
        var (host, window) = ShowAttached();
        var asked = 0;
        var closing = 0;
        host.Closing += () => closing++;
        host.SetCloseGuard(() => false, () =>
        {
            asked++;
            return Task.FromResult(true);
        });

        window.Close();

        Assert.False(window.IsVisible);
        Assert.Equal(0, asked);
        Assert.Equal(1, closing);
    }

    [AvaloniaFact]
    public void DispatchReportsWhetherAnyHandlerHandledTheMessage()
    {
        var host = new MainWindowHost();
        var calls = new List<string>();
        host.AddMessageHandler((msg, _) => { calls.Add("first"); return false; });
        host.AddMessageHandler((msg, wParam) => { calls.Add("second"); return msg == 0x0312 && wParam == 3001; });

        Assert.True(host.DispatchMessage(0x0312, 3001));
        Assert.False(host.DispatchMessage(0x007E, 0));
        Assert.Equal(["first", "second", "first", "second"], calls);
    }

    [AvaloniaFact]
    public void HandlersChangedDuringDispatchApplyFromTheNextMessage()
    {
        var host = new MainWindowHost();
        var calls = new List<string>();
        Func<uint, nint, bool> late = (_, _) => { calls.Add("late"); return false; };
        Func<uint, nint, bool>? self = null;
        self = (_, _) =>
        {
            calls.Add("self");
            host.RemoveMessageHandler(self!);
            host.AddMessageHandler(late);
            return false;
        };
        host.AddMessageHandler(self);
        host.AddMessageHandler((_, _) => { calls.Add("other"); return false; });

        host.DispatchMessage(0x0312, 0);
        Assert.Equal(["self", "other"], calls);

        calls.Clear();
        host.DispatchMessage(0x0312, 0);
        Assert.Equal(["other", "late"], calls);
    }

    [AvaloniaFact]
    public void RemovingAnUnknownHandlerKeepsTheOthers()
    {
        var host = new MainWindowHost();
        var calls = 0;
        host.AddMessageHandler((_, _) => { calls++; return false; });
        host.RemoveMessageHandler((_, _) => true);

        host.DispatchMessage(0x0312, 0);
        Assert.Equal(1, calls);
    }
}
