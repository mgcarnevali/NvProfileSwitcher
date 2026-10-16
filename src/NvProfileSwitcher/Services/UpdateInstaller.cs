namespace NvProfileSwitcher.Services;

/// <summary>
/// Prepares, launches, cancels and discards an update. Implemented by <see cref="UpdateInstaller"/>
/// over <see cref="UpdateService"/>; it lets the update flow be tested without downloads or processes.
/// </summary>
internal interface IUpdateInstaller
{
    Task<PreparedUpdate> PrepareAsync(UpdateRelease release, IProgress<string> progress);

    LaunchedUpdate Launch(PreparedUpdate update);

    void Cancel(LaunchedUpdate launched);

    void Discard(PreparedUpdate update);
}

internal sealed class UpdateInstaller : IUpdateInstaller
{
    public Task<PreparedUpdate> PrepareAsync(UpdateRelease release, IProgress<string> progress) =>
        UpdateService.PrepareAsync(release, progress);

    public LaunchedUpdate Launch(PreparedUpdate update) => UpdateService.Launch(update);

    public void Cancel(LaunchedUpdate launched) => UpdateService.Cancel(launched);

    public void Discard(PreparedUpdate update) => UpdateService.Discard(update);
}
