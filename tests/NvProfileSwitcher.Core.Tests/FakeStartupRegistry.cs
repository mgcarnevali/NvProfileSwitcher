using NvProfileSwitcher.Platform;

namespace NvProfileSwitcher.Core.Tests;

// Records the Windows startup entry writes instead of touching the registry.
internal sealed class FakeStartupRegistry : IStartupRegistry
{
    public List<string> Log { get; } = [];
    public Exception? Failure { get; set; }

    public void SetRunValue(string name, string command)
    {
        if (Failure is not null)
            throw Failure;
        Log.Add($"set {name}={command}");
    }

    public void DeleteRunValue(string name)
    {
        if (Failure is not null)
            throw Failure;
        Log.Add($"delete {name}");
    }
}
