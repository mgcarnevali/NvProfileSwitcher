using System.Diagnostics;
using NvProfileSwitcher.Core;

namespace NvProfileSwitcher.Platform;

/// <summary>
/// The version of the NVIDIA driver installed in Windows, from the file version of the NVIDIA API
/// library in the system folder. It does not load the library, so it shows a driver installed
/// while the application runs, which keeps using the library it loaded at start.
/// </summary>
internal static class InstalledDriverVersion
{
    /// <summary>The installed driver version, as "617.42", or null when it cannot be read.</summary>
    public static string? Read()
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
