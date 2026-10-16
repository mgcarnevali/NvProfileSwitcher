using Avalonia.Threading;

namespace NvProfileSwitcher.Services;

/// <summary>
/// Detects display topology changes from WM_DISPLAYCHANGE, and the return from sleep or
/// hibernation from WM_POWERBROADCAST, and reports them once the messages have settled. It does
/// not enumerate displays or change any profile.
/// </summary>
internal sealed class DisplayTopologyService
{
    private const uint WmDisplayChange = 0x007E;
    private const uint WmPowerBroadcast = 0x0218;
    private const nint PbtApmResumeSuspend = 0x0007;
    private const nint PbtApmResumeAutomatic = 0x0012;
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(500);

    /// <summary>How long Windows is given to make the monitor identities readable before the one retry.</summary>
    internal static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private readonly DispatcherTimer debounceTimer = new() { Interval = Debounce };

    // Whether the change being reported was already reported again once.
    private bool retried;

    // Whether the messages being settled include a return from sleep or hibernation.
    private bool resumed;

    public DisplayTopologyService()
    {
        debounceTimer.Tick += (_, _) =>
        {
            debounceTimer.Stop();
            if (resumed)
            {
                resumed = false;
                Resumed?.Invoke();
            }
            else
            {
                TopologyChanged?.Invoke();
            }
        };
    }

    public event Action? TopologyChanged;

    /// <summary>
    /// Raised once the messages of a return from sleep or hibernation have settled, in place of
    /// <see cref="TopologyChanged"/>: Windows sends both resume notifications and usually a display
    /// change too, and they are reported once.
    /// </summary>
    public event Action? Resumed;

    /// <summary>
    /// Window message handler. A single topology change or resume can produce several messages,
    /// so each one restarts the debounce. The message is never marked as handled.
    /// </summary>
    public bool HandleMessage(uint msg, nint wParam)
    {
        if (msg == WmDisplayChange || (msg == WmPowerBroadcast && wParam is PbtApmResumeAutomatic or PbtApmResumeSuspend))
        {
            resumed |= msg == WmPowerBroadcast;
            retried = false;
            Restart(Debounce);
        }

        return false;
    }

    /// <summary>
    /// Reports the last change again after <see cref="RetryDelay"/>, once per change: a monitor
    /// whose identity Windows could not report yet sends no further message when it can. A new
    /// change restarts the debounce instead.
    /// </summary>
    public void RetryOnce()
    {
        if (retried)
            return;

        retried = true;
        Restart(RetryDelay);
    }

    public void Stop() => debounceTimer.Stop();

    private void Restart(TimeSpan interval)
    {
        debounceTimer.Stop();
        debounceTimer.Interval = interval;
        debounceTimer.Start();
    }
}
