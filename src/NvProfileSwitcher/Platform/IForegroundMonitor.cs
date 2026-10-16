namespace NvProfileSwitcher.Platform;

/// <summary>
/// Reports foreground window changes, debounced. <see cref="ForegroundWatcher"/> implements
/// it with a WinEvent hook; tests use a fake without Win32 hooks.
/// </summary>
internal interface IForegroundMonitor : IDisposable
{
    event Action? Changed;

    /// <summary>Starts reporting changes and reports the current foreground once.</summary>
    void Start();

    void Stop();
}
