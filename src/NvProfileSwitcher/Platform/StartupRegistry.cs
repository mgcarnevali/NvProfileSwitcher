using Microsoft.Win32;

namespace NvProfileSwitcher.Platform;

internal sealed class StartupRegistry : IStartupRegistry
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public void SetRunValue(string name, string command)
    {
        using var runKey = OpenRunKey();
        runKey.SetValue(name, command, RegistryValueKind.String);
    }

    public void DeleteRunValue(string name)
    {
        using var runKey = OpenRunKey();
        runKey.DeleteValue(name, throwOnMissingValue: false);
    }

    private static RegistryKey OpenRunKey() =>
        Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Could not open the Windows startup registry key.");
}
