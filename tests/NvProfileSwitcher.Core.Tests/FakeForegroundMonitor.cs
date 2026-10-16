using NvProfileSwitcher.Platform;

namespace NvProfileSwitcher.Core.Tests;

// A foreground monitor without Win32 hooks; tests raise foreground changes explicitly.
internal sealed class FakeForegroundMonitor(List<string>? log = null) : IForegroundMonitor
{
    public event Action? Changed;

    public bool Running { get; private set; }
    public int StartCount { get; private set; }
    public int StopCount { get; private set; }
    public int DisposeCount { get; private set; }

    public void Start()
    {
        StartCount++;
        Running = true;
    }

    public void Stop()
    {
        StopCount++;
        Running = false;
    }

    public void Dispose()
    {
        DisposeCount++;
        Running = false;
        log?.Add("monitor dispose");
    }

    public void RaiseChanged() => Changed?.Invoke();
}
