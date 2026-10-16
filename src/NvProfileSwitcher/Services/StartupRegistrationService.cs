using NvProfileSwitcher.Models;
using NvProfileSwitcher.Platform;

namespace NvProfileSwitcher.Services;

/// <summary>
/// Keeps the Windows startup entry in line with Start with Windows and Start minimized:
/// the quoted executable path, followed by --minimized when the window starts hidden.
/// </summary>
internal sealed class StartupRegistrationService(AppSettings settings, IStartupRegistry registry, string? executablePath)
{
    private const string ValueName = "NvProfileSwitcher";

    /// <summary>
    /// Writes or deletes the entry. Failures are ignored: the original only reported them
    /// in its hidden status text.
    /// </summary>
    public void Apply()
    {
        try
        {
            if (!settings.StartWithWindows)
            {
                registry.DeleteRunValue(ValueName);
                return;
            }

            if (string.IsNullOrWhiteSpace(executablePath))
                throw new InvalidOperationException("Could not determine the NvProfileSwitcher executable path.");

            var command = $"\"{executablePath}\"";
            if (settings.StartMinimized)
                command += " --minimized";

            registry.SetRunValue(ValueName, command);
        }
        catch (Exception)
        {
        }
    }
}
