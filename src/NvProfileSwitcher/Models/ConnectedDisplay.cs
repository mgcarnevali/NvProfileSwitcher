namespace NvProfileSwitcher;

/// <summary>
/// A monitor connected to the desktop, as Windows reports it, whatever adapter drives it. Its
/// resolution can be changed through Windows; its colors only when the NVIDIA API controls it
/// (<see cref="Nvidia"/>). Each enumeration gives a new record, so the capabilities always match
/// the displays they belong to.
/// </summary>
internal sealed record ConnectedDisplay(
    string GdiName,
    string Name,
    bool Primary,
    string MonitorId,
    int Width = 0,
    int Height = 0,
    int RefreshRate = 0)
{
    /// <summary>What Windows reports about the monitor; null when it could not be read.</summary>
    public MonitorDescriptor? Monitor { get; init; }

    /// <summary>
    /// The physical identity of a monitor that shares it with another one, which therefore has
    /// its connection identity as <see cref="MonitorId"/>; null otherwise.
    /// </summary>
    public string? PhysicalMonitorId { get; init; }

    /// <summary>The NVIDIA API's handle of the display; null when no NVIDIA driver controls it.</summary>
    public NvidiaHandle? Nvidia { get; init; }

    /// <summary>Whether this application can read and write the display's colors.</summary>
    public bool CanAdjustColors => Nvidia is not null;

    public string Label => $"{Name}{(Primary ? " (Primary)" : "")}";

    public string SelectorLabel
    {
        get
        {
            var label = Label;
            if (Width > 0 && Height > 0)
                label += $" · {Width}×{Height}";
            if (RefreshRate > 0)
                label += $" · {RefreshRate} Hz";
            if (!CanAdjustColors)
                label += " · Colors unavailable";
            return label;
        }
    }

    public override string ToString() => Label;
}

/// <summary>The NVIDIA API's handle and id of a display: vibrance and hue use the handle, gamma the id.</summary>
internal readonly record struct NvidiaHandle(nint Handle, uint DisplayId);

/// <summary>
/// The monitor of a display as Windows reports it: its hardware id (model), its device instance
/// (the connection) and its EDID serial number, or null when the EDID has none.
/// </summary>
internal sealed record MonitorDescriptor(string Hardware, string Instance, string? Serial);
