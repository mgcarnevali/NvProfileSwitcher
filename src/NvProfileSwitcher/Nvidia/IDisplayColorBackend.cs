namespace NvProfileSwitcher.Nvidia;

/// <summary>
/// The NVIDIA API operations used by the application: colors, and the desktop resolution of a
/// display. Implemented by <see cref="NvApi"/>; it allows the services that use NVAPI to be tested
/// without an NVIDIA GPU.
/// </summary>
internal interface IDisplayColorBackend : IDisposable
{
    string DriverVersion { get; }

    IReadOnlyList<NvidiaDisplay> EnumerateDisplays();

    int? GetVibrance(NvidiaDisplay display);

    int? GetHue(NvidiaDisplay display);

    /// <summary>
    /// The id that names the display's NVIDIA Control Panel settings in the registry
    /// (<see cref="Core.NvTweakColorCore"/>), or null when the driver does not report it.
    /// </summary>
    uint? GetNvTweakDeviceId(NvidiaDisplay display);

    /// <summary>
    /// Applies digital vibrance, hue and the brightness, contrast and gamma ramp to a
    /// display, stopping at the first setting the driver rejects. Returns false then.
    /// </summary>
    bool Apply(NvidiaDisplay display, int vibrance, double brightness, double contrast, double gamma, int hue);

    /// <summary>The desktop resolution of the display, as the NVIDIA driver reports it; null when it cannot be read.</summary>
    DisplayResolution? GetResolution(NvidiaDisplay display);

    /// <summary>
    /// Sets the desktop resolution of the display through the NVIDIA driver, for this session only:
    /// nothing else of its configuration changes. Validated before it is applied; false when the
    /// driver rejects it or the display is not a single-display path.
    /// </summary>
    bool SetResolution(NvidiaDisplay display, DisplayResolution resolution);

    /// <summary>The resolutions the display offers at its current color depth, largest first; read only.</summary>
    IReadOnlyList<DisplayResolution> GetSupportedResolutions(NvidiaDisplay display);
}
