using System.Text;
using NvProfileSwitcher.Nvidia;

namespace NvProfileSwitcher.Core;

/// <summary>
/// The identity of a connected monitor, from what Windows reports about it. A monitor with a
/// serial number that no other monitor shares has a physical identity, "{hardware}-{serial}",
/// which follows it to any connection. Any other monitor whose EDID could be read has a
/// connection identity, "{hardware}@{instance}": the monitor model on the connection Windows
/// identifies it by, so identical monitors without a serial number keep their own values. A
/// monitor whose identity could not be read has none: it is left out of the displays, and no
/// identity is made up for it.
/// </summary>
internal static class MonitorIdentityCore
{
    private const int EdidBlockLength = 128;

    /// <summary>
    /// The hardware id and the device instance of a monitor device path such as
    /// <c>\\?\DISPLAY#DEL4321#5&amp;1a2b3c&amp;0&amp;UID4352#{guid}</c>, or null when the
    /// path does not have that form.
    /// </summary>
    internal static (string Hardware, string Instance)? ParseDevicePath(string? devicePath)
    {
        var parts = (devicePath ?? string.Empty).Split('#');
        if (parts.Length < 4 || string.IsNullOrWhiteSpace(parts[1]) || string.IsNullOrWhiteSpace(parts[2]))
            return null;
        return (parts[1], parts[2]);
    }

    /// <summary>
    /// Whether the EDID is complete enough to tell whether the monitor has a serial number.
    /// An EDID that could not be read is not a monitor without a serial number.
    /// </summary>
    internal static bool IsReadable(byte[]? edid) => edid is { Length: >= EdidBlockLength };

    /// <summary>
    /// The serial number of a readable EDID: the serial number descriptor, or else the numeric
    /// serial number unless it is 0 or 0xFFFFFFFF; null when it has none.
    /// </summary>
    internal static string? Serial(byte[] edid)
    {
        for (var offset = 54; offset + 18 <= edid.Length && offset < 126; offset += 18)
        {
            if (edid[offset] != 0 || edid[offset + 1] != 0 || edid[offset + 2] != 0 || edid[offset + 3] != 0xFF) continue;
            var text = new StringBuilder();
            for (var i = offset + 5; i < offset + 18; i++)
            {
                var c = (char)edid[i];
                if (c is '\0' or '\r' or '\n') break;
                text.Append(c);
            }

            var serial = text.ToString().TrimEnd(' ', '\t');
            if (serial.Length > 0) return serial;
        }

        var numeric = (uint)(edid[12] | (edid[13] << 8) | (edid[14] << 16) | (edid[15] << 24));
        return numeric is 0 or 0xFFFFFFFF ? null : numeric.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static string PhysicalId(MonitorDescriptor monitor) => $"{monitor.Hardware}-{monitor.Serial}";

    internal static string ConnectionId(MonitorDescriptor monitor) => $"{monitor.Hardware}@{monitor.Instance}";

    /// <summary>Whether a saved monitor id is a connection identity.</summary>
    internal static bool IsConnectionId(string monitorId) => monitorId.Contains('@');

    /// <summary>The hardware id (monitor model) of a connection identity.</summary>
    internal static string ConnectionHardware(string monitorId) => monitorId[..monitorId.IndexOf('@')];

    /// <summary>
    /// Gives every enumerated display its monitor id and leaves out the displays without an
    /// identity. A physical identity that two connected monitors share, or that was found
    /// shared before (<paramref name="nonUniqueIds"/>), is not an identity: those monitors take
    /// their connection identity, and keep the physical one as <see cref="NvidiaDisplay.PhysicalMonitorId"/>
    /// so its saved values can be carried over. Returns the physical identities found shared for
    /// the first time in this enumeration.
    /// </summary>
    internal static MonitorIdentification Identify(IReadOnlyList<NvidiaDisplay> enumerated, IReadOnlySet<string> nonUniqueIds)
    {
        var physicalCounts = enumerated
            .Where(display => display.Monitor?.Serial is not null)
            .GroupBy(display => PhysicalId(display.Monitor!), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var newlyShared = physicalCounts
            .Where(entry => entry.Value > 1 && !nonUniqueIds.Contains(entry.Key))
            .Select(entry => entry.Key)
            .ToList();

        var identified = new List<NvidiaDisplay>();
        var unidentified = 0;
        foreach (var display in enumerated)
        {
            if (display.Monitor is not { } monitor)
            {
                unidentified++;
                continue;
            }

            if (monitor.Serial is null)
            {
                identified.Add(display with { MonitorId = ConnectionId(monitor), PhysicalMonitorId = null });
                continue;
            }

            var physical = PhysicalId(monitor);
            identified.Add(physicalCounts[physical] == 1 && !nonUniqueIds.Contains(physical)
                ? display with { MonitorId = physical, PhysicalMonitorId = null }
                : display with { MonitorId = ConnectionId(monitor), PhysicalMonitorId = physical });
        }

        return new MonitorIdentification(identified, unidentified, newlyShared);
    }
}

/// <summary>The identified displays of an enumeration, how many had no identity, and the physical identities found shared.</summary>
internal sealed record MonitorIdentification(
    IReadOnlyList<NvidiaDisplay> Displays,
    int UnidentifiedCount,
    IReadOnlyList<string> NewlySharedIds);
