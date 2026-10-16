namespace NvProfileSwitcher.Nvidia;

/// <summary>
/// The NVIDIA API operations used by the application. Implemented by <see cref="NvApi"/>;
/// it allows the services that use NVAPI to be tested without an NVIDIA GPU.
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
}
