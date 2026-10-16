namespace NvProfileSwitcher.Platform;

/// <summary>
/// Writes the Windows startup entry. <see cref="StartupRegistry"/> uses the current user's
/// Run key; tests use a fake.
/// </summary>
internal interface IStartupRegistry
{
    void SetRunValue(string name, string command);

    void DeleteRunValue(string name);
}
