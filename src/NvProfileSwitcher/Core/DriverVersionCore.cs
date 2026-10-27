namespace NvProfileSwitcher.Core;

/// <summary>The NVIDIA driver version from the Windows version of a driver file.</summary>
internal static class DriverVersionCore
{
    /// <summary>
    /// The NVIDIA version of a Windows driver file version: the last digit of the third part and
    /// the four digits of the fourth, as "617.42" for 32.0.16.1742. Null for a version without
    /// that form.
    /// </summary>
    internal static string? FromFileVersion(int major, int minor, int build, int revision)
    {
        if (major <= 0 || minor < 0 || build < 0 || revision is < 0 or > 9999)
            return null;

        var number = build % 10 * 10000 + revision;
        return number == 0 ? null : $"{number / 100}.{number % 100:D2}";
    }

    /// <summary>
    /// The NVIDIA version of the display adapters Windows lists as (provider, Windows driver
    /// version, as "32.0.16.2101"): the newest one whose provider is NVIDIA. Null when there is
    /// none with a version of that form.
    /// </summary>
    internal static string? FromDisplayAdapters(IEnumerable<(string? Provider, string? Version)> adapters)
    {
        Version? newest = null;
        foreach (var (provider, value) in adapters)
        {
            if (!string.Equals(provider?.Trim(), "NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                !TryParseDriverVersion(value, out var version) ||
                FromFileVersion(version.Major, version.Minor, version.Build, version.Revision) is null)
                continue;

            if (newest is null || version > newest)
                newest = version;
        }

        return newest is null ? null : FromFileVersion(newest.Major, newest.Minor, newest.Build, newest.Revision);
    }

    // A Windows driver version has exactly four parts.
    private static bool TryParseDriverVersion(string? value, out Version version)
    {
        version = new Version();
        var parts = value?.Trim().Split('.');
        if (parts is not { Length: 4 })
            return false;

        var numbers = new int[4];
        for (var i = 0; i < 4; i++)
        {
            if (!int.TryParse(parts[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out numbers[i]))
                return false;
        }

        version = new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
        return true;
    }
}
