using Avalonia.Threading;
using NvProfileSwitcher.Platform;
using System.Runtime.InteropServices;

namespace NvProfileSwitcher;

internal sealed class ForegroundWatcher : IForegroundMonitor
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WineventOutOfContext = 0x0000;
    private const int DebounceMilliseconds = 150;

    private readonly WinEventProc callback;
    private readonly DispatcherTimer debounceTimer;
    private IntPtr hook;

    public event Action? Changed;

    public ForegroundWatcher()
    {
        callback = OnWinEvent;
        debounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(DebounceMilliseconds)
        };
        debounceTimer.Tick += (_, _) =>
        {
            debounceTimer.Stop();
            Changed?.Invoke();
        };
    }

    public void Start()
    {
        if (hook != IntPtr.Zero) return;

        hook = SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            IntPtr.Zero,
            callback,
            0,
            0,
            WineventOutOfContext);

        // Evaluate the current foreground once after startup/resume.
        debounceTimer.Stop();
        debounceTimer.Start();
    }

    public void Stop()
    {
        if (hook != IntPtr.Zero)
        {
            UnhookWinEvent(hook);
            hook = IntPtr.Zero;
        }

        debounceTimer.Stop();
    }

    private void OnWinEvent(
        IntPtr hookHandle,
        uint eventType,
        IntPtr hwnd,
        int objectId,
        int childId,
        uint threadId,
        uint eventTime)
    {
        debounceTimer.Stop();
        debounceTimer.Start();
    }

    public void Dispose() => Stop();

    private delegate void WinEventProc(
        IntPtr hookHandle,
        uint eventType,
        IntPtr hwnd,
        int objectId,
        int childId,
        uint threadId,
        uint eventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr module,
        WinEventProc callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);
}
