using CommunityToolkit.Mvvm.ComponentModel;
using NvProfileSwitcher.Core;
using NvProfileSwitcher.Nvidia;

namespace NvProfileSwitcher.Services;

/// <summary>
/// Owns the NVIDIA API: its initialization, its status, the driver version, which displays it
/// controls and their colors. The displays themselves belong to <see cref="DisplayService"/>.
/// The driver version is the one installed in Windows (<paramref name="installedDriverVersion"/>),
/// or the one the API reports when it cannot be read: the API library loaded at start keeps
/// reporting its own driver after another one is installed.
/// It knows nothing about profiles, configuration, switching or the user interface.
/// Without an initialized API, the per-display operations return no value (null or false), and
/// no display is reported as controlled by it.
/// </summary>
internal sealed partial class NvidiaService(
    Func<IDisplayColorBackend> createBackend,
    Func<string?>? installedDriverVersion = null) : ObservableObject, IDisposable
{
    private IDisplayColorBackend? backend;

    /// <summary>The result of the last initialization. Starts as available, as the original status bar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAvailable))]
    public partial NvidiaStatus Status { get; private set; } = NvidiaStatus.Available;

    /// <summary>Whether the last initialization succeeded.</summary>
    public bool IsAvailable => Status == NvidiaStatus.Available;

    [ObservableProperty] public partial string DriverVersion { get; private set; } = "Unknown";

    public bool IsInitialized => backend is not null;

    /// <summary>
    /// Initializes the NVIDIA API. A new instance replaces the current one only after its own
    /// initialization succeeded, so the API stays initialized while the previous instance is
    /// released and no instance is left behind. When initialization fails, the exception
    /// propagates and the current instance remains in use.
    /// </summary>
    public void Initialize()
    {
        var created = createBackend();
        var previous = backend;
        backend = created;
        previous?.Dispose();

        Status = NvidiaStatus.Available;
        DriverVersion = installedDriverVersion?.Invoke() ?? created.DriverVersion;
    }

    /// <summary>
    /// <see cref="Initialize"/>, reporting a failure instead of throwing it: the rest of the
    /// application works without the NVIDIA API. False when it failed.
    /// </summary>
    public bool TryInitialize()
    {
        try
        {
            Initialize();
            return true;
        }
        catch (Exception exception)
        {
            ReportUnavailable(exception);
            return false;
        }
    }

    /// <summary>
    /// Reports that the NVIDIA API cannot be used, without releasing the current instance: not
    /// detected when no NVIDIA driver or GPU is present (<paramref name="failure"/> says so), and
    /// otherwise unavailable, usually for a while, as when the driver is being installed.
    /// </summary>
    public void ReportUnavailable(Exception? failure = null)
    {
        Status = failure is DllNotFoundException or NvApiInitializationException { Status: NvApiInitializationException.NvidiaDeviceNotFound }
            ? NvidiaStatus.NotDetected
            : NvidiaStatus.Unavailable;
        DriverVersion = "Unknown";
    }

    /// <summary>
    /// Updates the driver version when the installed driver, or else the API, reports a known,
    /// different version.
    /// </summary>
    public void RefreshDriverVersion()
    {
        if (backend is null)
            return;

        var version = installedDriverVersion?.Invoke() ?? backend.DriverVersion;
        if (!string.Equals(version, "Unknown", StringComparison.Ordinal) &&
            !string.Equals(DriverVersion, version, StringComparison.Ordinal))
            DriverVersion = version;
    }

    /// <summary>
    /// The NVIDIA handle of the display shown on <paramref name="gdiName"/>, or null when the API is
    /// not available, does not control that display or fails: the display then has no colors. An
    /// API reported unavailable controls no display, even while a previous instance is kept, so the
    /// colors are unavailable until it is initialized again, as the status bar says.
    /// </summary>
    public NvidiaHandle? ResolveDisplay(string gdiName)
    {
        if (Status != NvidiaStatus.Available)
            return null;

        try
        {
            return backend?.ResolveDisplay(gdiName);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public int? GetVibrance(ConnectedDisplay display) => backend?.GetVibrance(display);

    public int? GetHue(ConnectedDisplay display) => backend?.GetHue(display);

    public uint? GetNvTweakDeviceId(ConnectedDisplay display) => backend?.GetNvTweakDeviceId(display);

    /// <summary>Applies color values to a display; false when the API rejects a setting or is not initialized.</summary>
    public bool Apply(ConnectedDisplay display, int vibrance, double brightness, double contrast, double gamma, int hue) =>
        backend?.Apply(display, vibrance, brightness, contrast, gamma, hue) ?? false;

    public void Dispose()
    {
        backend?.Dispose();
        backend = null;
    }
}

/// <summary>Whether the NVIDIA API can be used.</summary>
internal enum NvidiaStatus
{
    Available,

    /// <summary>No NVIDIA driver, or no active NVIDIA GPU.</summary>
    NotDetected,

    /// <summary>The driver is there but the API could not be initialized.</summary>
    Unavailable,
}
