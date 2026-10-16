using Microsoft.Win32;
using NvProfileSwitcher.Core;

namespace NvProfileSwitcher.Nvidia;

/// <summary>
/// Reads, never writes, the color settings the NVIDIA Control Panel keeps for a display in the
/// registry (<see cref="NvTweakColorCore"/>). <see cref="NvTweakColorCore.Parse"/> decides whether
/// they can be used.
/// </summary>
internal static class NvTweakColorRegistry
{
    /// <summary>
    /// The nine values of <paramref name="deviceId"/>, each null when it does not exist, or null
    /// when the key does not exist or cannot be read.
    /// </summary>
    public static IReadOnlyList<NvTweakValue?>? Read(uint deviceId)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(NvTweakColorCore.KeyPath(deviceId), writable: false);
            if (key is null)
                return null;

            var names = new HashSet<string>(key.GetValueNames(), StringComparer.OrdinalIgnoreCase);
            var values = new NvTweakValue?[NvTweakColorCore.ValueCount];
            for (var i = 0; i < values.Length; i++)
            {
                var name = NvTweakColorCore.ValueName(i);
                if (names.Contains(name))
                    values[i] = new NvTweakValue(key.GetValueKind(name), key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames));
            }
            return values;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
