using NvProfileSwitcher.Services;

namespace NvProfileSwitcher.Core.Tests;

// Records what the update flow does with an update instead of downloading files or starting
// processes. Preparing can be held open by the test, and preparing or launching can fail.
internal sealed class FakeUpdateInstaller : IUpdateInstaller
{
    public List<string> Log { get; } = [];
    public TaskCompletionSource? PendingPreparation { get; set; }
    public Exception? PrepareFailure { get; set; }
    public Exception? LaunchFailure { get; set; }

    public async Task<PreparedUpdate> PrepareAsync(UpdateRelease release, IProgress<string> progress)
    {
        Log.Add($"prepare {release.Version}");
        if (PendingPreparation is { } pending)
            await pending.Task;
        if (PrepareFailure is { } failure)
            throw failure;
        return new PreparedUpdate(release.IsInstaller, $"root {release.Version}", "helper.exe", "--apply-update", "helper");
    }

    public LaunchedUpdate Launch(PreparedUpdate update)
    {
        if (LaunchFailure is { } failure)
            throw failure;
        Log.Add($"launch {update.Root}");
        return new LaunchedUpdate(update, Process: null);
    }

    public void Cancel(LaunchedUpdate launched) => Log.Add($"cancel {launched.Update.Root}");

    public void Discard(PreparedUpdate update) => Log.Add($"discard {update.Root}");
}
