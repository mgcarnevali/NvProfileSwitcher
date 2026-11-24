namespace NvProfileSwitcher.Nvidia;

/// <summary>
/// The NVIDIA API operations used by the application. Implemented by <see cref="NvApi"/>;
/// it allows the services that use NVAPI to be tested without an NVIDIA GPU. The displays
/// themselves come from Windows (<see cref="Platform.IDisplayEnumerator"/>); the API only tells
/// which of them it controls and reads and writes their colors.
/// </summary>
internal interface IDisplayColorBackend : IDisposable
{
    string DriverVersion { get; }

    /// <summary>
    /// The NVIDIA handle and id of the display shown on the GDI device <paramref name="gdiName"/>,
    /// or null when the NVIDIA driver does not control it.
    /// </summary>
    NvidiaHandle? ResolveDisplay(string gdiName);

    int? GetVibrance(ConnectedDisplay display);

    int? GetHue(ConnectedDisplay display);

    /// <summary>
    /// The id that names the display's NVIDIA Control Panel settings in the registry
    /// (<see cref="Core.NvTweakColorCore"/>), or null when the driver does not report it.
    /// </summary>
    uint? GetNvTweakDeviceId(ConnectedDisplay display);

    /// <summary>
    /// Applies digital vibrance, hue and the brightness, contrast and gamma ramp to a
    /// display, stopping at the first setting the driver rejects. Returns false then.
    /// </summary>
    bool Apply(ConnectedDisplay display, int vibrance, double brightness, double contrast, double gamma, int hue);
}
