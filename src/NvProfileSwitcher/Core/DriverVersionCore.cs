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
}
