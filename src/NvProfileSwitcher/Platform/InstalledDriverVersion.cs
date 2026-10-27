using System.Diagnostics;
using Microsoft.Win32;
using NvProfileSwitcher.Core;

namespace NvProfileSwitcher.Platform;

/// <summary>
/// The version of the NVIDIA driver installed in Windows. It comes first from the driver version
/// Windows records for the NVIDIA display adapter when the driver is installed, and otherwise from
/// the file version of the NVIDIA API library in the system folder. The library is only a
/// fallback: this process loads it at start and keeps it loaded, and after another driver was
/// installed its file version kept reporting the driver the application started with (most likely
/// because a version query for a path already loaded in the process reads the loaded image).
/// </summary>
internal static class InstalledDriverVersion
{
    // The display adapters device class.
    private const string DisplayAdaptersKey =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    /// <summary>The installed driver version, as "617.42", or null when it cannot be read.</summary>
    public static string? Read() => Read(FromRegistry, FromLibrary);

    // The library is read only when the registry gives no version.
    internal static string? Read(Func<string?> fromRegistry, Func<string?> fromLibrary) =>
        fromRegistry() ?? fromLibrary();

    private static string? FromRegistry()
    {
        try
        {
            using var adapters = Registry.LocalMachine.OpenSubKey(DisplayAdaptersKey);
            if (adapters is null)
                return null;

            var found = new List<(string?, string?)>();
            foreach (var name in adapters.GetSubKeyNames())
            {
                // Some subkeys, such as Properties, cannot be read without elevation.
                try
                {
                    using var adapter = adapters.OpenSubKey(name);
                    if (adapter is not null)
                        found.Add((adapter.GetValue("ProviderName") as string, adapter.GetValue("DriverVersion") as string));
                }
                catch (Exception)
                {
                }
            }

            return DriverVersionCore.FromDisplayAdapters(found);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? FromLibrary()
    {
        try
        {
            var path = Path.Combine(Environment.SystemDirectory, "nvapi64.dll");
            if (!File.Exists(path))
                return null;

            var info = FileVersionInfo.GetVersionInfo(path);
            return DriverVersionCore.FromFileVersion(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
