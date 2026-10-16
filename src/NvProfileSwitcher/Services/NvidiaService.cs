using CommunityToolkit.Mvvm.ComponentModel;
using NvProfileSwitcher.Core;
using NvProfileSwitcher.Nvidia;

namespace NvProfileSwitcher.Services;

/// <summary>
/// Owns the NVIDIA API: its initialization, the enumerated displays and the driver version.
/// The driver version is the one installed in Windows (<paramref name="installedDriverVersion"/>),
/// or the one the API reports when it cannot be read: the API library loaded at start keeps
/// reporting its own driver after another one is installed.
/// It knows nothing about profiles, configuration, switching or the user interface.
/// Without an initialized API, the per-display operations return no value (null or false)
/// while <see cref="RefreshDisplays"/>, which replaces the display list, throws; callers
/// check <see cref="IsInitialized"/> first, as the original checked for a null API.
/// </summary>
internal sealed partial class NvidiaService(
    Func<IDisplayColorBackend> createBackend,
    Func<string?>? installedDriverVersion = null) : ObservableObject, IDisposable
{
    private IDisplayColorBackend? backend;

    /// <summary>Whether the last initialization succeeded. Starts as available, as the original status bar.</summary>
    [ObservableProperty] public partial bool IsAvailable { get; private set; } = true;

    [ObservableProperty] public partial string DriverVersion { get; private set; } = "Unknown";

    public bool IsInitialized => backend is not null;

    public IReadOnlyList<NvidiaDisplay> Displays { get; private set; } = [];

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

        IsAvailable = true;
        DriverVersion = installedDriverVersion?.Invoke() ?? created.DriverVersion;
    }

    /// <summary>Reports that the NVIDIA API is unavailable without releasing the current instance.</summary>
    public void ReportUnavailable()
    {
        IsAvailable = false;
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

    /// <summary>How many enumerated displays have no identity and are left out of <see cref="Displays"/>.</summary>
    public int UnidentifiedCount { get; private set; }

    /// <summary>
    /// Enumerates the displays and keeps the ones with an identity (<see cref="MonitorIdentityCore"/>):
    /// a display without one is never written, edited, synchronized or saved. Returns the
    /// physical identities found shared by two connected monitors for the first time.
    /// </summary>
    public IReadOnlyList<string> RefreshDisplays(IReadOnlySet<string>? nonUniqueIds = null)
    {
        if (backend is null)
            throw new InvalidOperationException("The NVIDIA API is not initialized.");

        var identification = MonitorIdentityCore.Identify(backend.EnumerateDisplays(), nonUniqueIds ?? new HashSet<string>());
        Displays = identification.Displays;
        UnidentifiedCount = identification.UnidentifiedCount;
        return identification.NewlySharedIds;
    }

    public int? GetVibrance(NvidiaDisplay display) => backend?.GetVibrance(display);

    public int? GetHue(NvidiaDisplay display) => backend?.GetHue(display);

    public uint? GetNvTweakDeviceId(NvidiaDisplay display) => backend?.GetNvTweakDeviceId(display);

    /// <summary>Applies color values to a display; false when the API rejects a setting or is not initialized.</summary>
    public bool Apply(NvidiaDisplay display, int vibrance, double brightness, double contrast, double gamma, int hue) =>
        backend?.Apply(display, vibrance, brightness, contrast, gamma, hue) ?? false;

    public void Dispose()
    {
        backend?.Dispose();
        backend = null;
    }
}
