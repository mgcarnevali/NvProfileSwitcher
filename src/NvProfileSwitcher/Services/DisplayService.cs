using NvProfileSwitcher.Core;
using NvProfileSwitcher.Platform;

namespace NvProfileSwitcher.Services;

/// <summary>
/// Owns the connected displays: the monitors Windows reports, whatever adapter drives them, each
/// with its identity (<see cref="MonitorIdentityCore"/>) and its capabilities. A display's colors
/// can be adjusted only when the NVIDIA API controls it; its resolution always. The capabilities
/// are read again with every enumeration, so a display that moves to another adapter, or an API
/// that becomes available or stops responding, is reflected by the next refresh. It works with
/// or without the NVIDIA API.
/// </summary>
internal sealed class DisplayService(IDisplayEnumerator enumerator, NvidiaService nvidia)
{
    /// <summary>The identified displays of the last enumeration, primary first.</summary>
    public IReadOnlyList<ConnectedDisplay> Displays { get; private set; } = [];

    /// <summary>How many enumerated displays have no identity and are left out of <see cref="Displays"/>.</summary>
    public int UnidentifiedCount { get; private set; }

    /// <summary>Whether the displays were enumerated once: the startup refresh ran.</summary>
    public bool IsEnumerated { get; private set; }

    /// <summary>
    /// Enumerates the displays, gives each the NVIDIA handle of the display, when the API controls
    /// it, and keeps the ones with an identity: a display without one is never written, edited,
    /// synchronized or saved. Returns the physical identities found shared by two connected
    /// monitors for the first time.
    /// </summary>
    public IReadOnlyList<string> Refresh(IReadOnlySet<string>? nonUniqueIds = null)
    {
        var enumerated = enumerator.EnumerateDisplays()
            .Select(display => display with { Nvidia = nvidia.ResolveDisplay(display.GdiName) })
            .ToList();
        var identification = MonitorIdentityCore.Identify(enumerated, nonUniqueIds ?? new HashSet<string>());
        Displays = identification.Displays;
        UnidentifiedCount = identification.UnidentifiedCount;
        IsEnumerated = true;
        return identification.NewlySharedIds;
    }
}
