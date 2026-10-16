using System.Globalization;
using Microsoft.Win32;

namespace NvProfileSwitcher.Core;

/// <summary>A value as read from the registry: its kind and its data; null when the value does not exist.</summary>
internal readonly record struct NvTweakValue(RegistryValueKind Kind, object? Data);

/// <summary>Brightness, contrast and gamma set in the NVIDIA Control Panel, in this application's units.</summary>
internal sealed record ControlPanelColors(double Brightness, double Contrast, double Gamma);

/// <summary>
/// The color settings the NVIDIA Control Panel keeps for each display in
/// HKCU\Software\NVIDIA Corporation\Global\NVTweak\Devices\{device id}-0\Color: nine DWORD values
/// named 3538946 to 3538954, brightness, contrast and gamma × 100 for red, green and blue. The
/// device id is the LUID NvAPI_SYS_GetLUIDFromDisplayID reports for the display. They use the
/// same scale as this application: brightness and contrast 80 to 120, gamma 30 to 280.
/// </summary>
internal static class NvTweakColorCore
{
    internal const int ValueCount = 9;
    private const int FirstValueName = 3538946;
    private const uint DeviceIdMask = 0xF0000000;

    internal static string KeyPath(uint deviceId) =>
        $@"Software\NVIDIA Corporation\Global\NVTweak\Devices\{deviceId.ToString(CultureInfo.InvariantCulture)}-0\Color";

    /// <summary>Names of the values, in order: brightness, contrast and gamma, each for red, green and blue.</summary>
    internal static string ValueName(int index) => (FirstValueName + index).ToString(CultureInfo.InvariantCulture);

    /// <summary>The device id from the second 32-bit word of the LUID NvAPI_SYS_GetLUIDFromDisplayID reports.</summary>
    internal static uint DeviceIdFromLuid(uint secondWord) => secondWord ^ DeviceIdMask;

    /// <summary>
    /// The settings of the nine values, or null unless all nine exist as DWORDs, each attribute has
    /// the same value for red, green and blue, and the values are in range. Nothing is averaged or
    /// taken in part: a display with any other settings keeps the neutral values.
    /// </summary>
    internal static ControlPanelColors? Parse(IReadOnlyList<NvTweakValue?>? values)
    {
        if (values is null || values.Count != ValueCount)
            return null;

        var attributes = new int[3];
        for (var attribute = 0; attribute < 3; attribute++)
        {
            int? common = null;
            for (var channel = 0; channel < 3; channel++)
            {
                if (values[attribute * 3 + channel] is not { Kind: RegistryValueKind.DWord, Data: int value })
                    return null;
                if (common is not null && common != value)
                    return null;
                common = value;
            }
            attributes[attribute] = common!.Value;
        }

        var (brightness, contrast, gamma) = (attributes[0], attributes[1], attributes[2]);
        if (brightness is < 80 or > 120 || contrast is < 80 or > 120 || gamma is < 30 or > 280)
            return null;

        return new ControlPanelColors(brightness, contrast, gamma / 100.0);
    }
}
