using NvProfileSwitcher.Shell;

namespace NvProfileSwitcher.Core.Tests;

// Records the dialogs and links requested instead of showing them.
internal sealed class FakeDialogService : IDialogService
{
    public List<(string Title, string Message)> Messages { get; } = [];
    public List<UpdateRelease> UpdatesShown { get; } = [];

    /// <summary>The install action of each update dialog shown, as its Update button runs it.</summary>
    public List<Func<IProgress<string>, Task<bool>>> UpdateInstalls { get; } = [];
    public List<Uri> OpenedUris { get; } = [];
    public int AboutCount { get; private set; }
    public Exception? UpdateDialogFailure { get; set; }

    // When set, messages stay open until the test completes it.
    public TaskCompletionSource? OpenMessage { get; set; }

    public Task ShowMessageAsync(string title, string message)
    {
        Messages.Add((title, message));
        return OpenMessage?.Task ?? Task.CompletedTask;
    }

    public Task ShowUpdateAvailableAsync(UpdateRelease release, Func<IProgress<string>, Task<bool>> install)
    {
        if (UpdateDialogFailure is not null)
            throw UpdateDialogFailure;
        UpdatesShown.Add(release);
        UpdateInstalls.Add(install);
        return Task.CompletedTask;
    }

    public Task ShowAboutAsync()
    {
        AboutCount++;
        return Task.CompletedTask;
    }

    public Task OpenUriAsync(Uri uri)
    {
        OpenedUris.Add(uri);
        return Task.CompletedTask;
    }

    // Editing dialogs: each request is recorded and answered with the configured answer.
    // A pending discard confirmation stays open until the test completes it.
    public List<bool> DiscardRequests { get; } = [];
    public bool DiscardAnswer { get; set; } = true;
    public TaskCompletionSource<bool>? PendingDiscard { get; set; }
    public List<string> RemoveRequests { get; } = [];
    public bool RemoveAnswer { get; set; } = true;
    public List<IReadOnlyList<string>> CopyRequests { get; } = [];
    public int? CopyAnswer { get; set; } = 0;
    public int RunningAppRequests { get; private set; }
    public string? RunningAppAnswer { get; set; }
    public int ExecutableRequests { get; private set; }
    public string? ExecutableAnswer { get; set; }

    public Task<bool> ConfirmDiscardAsync(bool isNewProfile)
    {
        DiscardRequests.Add(isNewProfile);
        return PendingDiscard?.Task ?? Task.FromResult(DiscardAnswer);
    }

    public Task<bool> ConfirmRemoveProfileAsync(string profileName)
    {
        RemoveRequests.Add(profileName);
        return Task.FromResult(RemoveAnswer);
    }

    public Task<int?> ChooseCopySourceAsync(IReadOnlyList<string> profileNames)
    {
        CopyRequests.Add(profileNames);
        return Task.FromResult(CopyAnswer);
    }

    public Task<string?> PickRunningAppAsync()
    {
        RunningAppRequests++;
        return Task.FromResult(RunningAppAnswer);
    }

    public Task<string?> PickExecutableAsync()
    {
        ExecutableRequests++;
        return Task.FromResult(ExecutableAnswer);
    }

    // Advanced Settings: the options of each request are recorded; the test runs the
    // dialog's actions through them and answers with AdvancedAnswer.
    public List<AdvancedSettingsOptions> AdvancedRequests { get; } = [];
    public AdvancedSettingsResult? AdvancedAnswer { get; set; }
    public Func<AdvancedSettingsOptions, Task>? AdvancedActions { get; set; }
    public List<IReadOnlyList<SavedDisplayInfo>> ManageDisplaysRequests { get; } = [];
    public Action<Action<string>>? ManageDisplaysActions { get; set; }

    // Backup pickers: null cancels; otherwise the content to import, or the stream exported to.
    public string? ImportFile { get; set; }
    public Exception? ImportOpenFailure { get; set; }
    public int ImportRequests { get; private set; }
    public MemoryStream? ExportFile { get; set; }
    public int ExportRequests { get; private set; }

    public async Task<AdvancedSettingsResult?> ShowAdvancedSettingsAsync(AdvancedSettingsOptions options)
    {
        AdvancedRequests.Add(options);
        if (AdvancedActions is not null)
            await AdvancedActions(options);
        return AdvancedAnswer;
    }

    public Task ShowManageDisplaysAsync(IReadOnlyList<SavedDisplayInfo> displays, Action<string> remove)
    {
        ManageDisplaysRequests.Add(displays);
        ManageDisplaysActions?.Invoke(remove);
        return Task.CompletedTask;
    }

    public Task<Func<Task<Stream>>?> PickImportFileAsync()
    {
        ImportRequests++;
        if (ImportFile is not { } content)
            return Task.FromResult<Func<Task<Stream>>?>(null);
        return Task.FromResult<Func<Task<Stream>>?>(() => ImportOpenFailure is { } failure
            ? Task.FromException<Stream>(failure)
            : Task.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content))));
    }

    public Task<Func<Task<Stream>>?> PickExportFileAsync()
    {
        ExportRequests++;
        if (ExportFile is not { } destination)
            return Task.FromResult<Func<Task<Stream>>?>(null);
        return Task.FromResult<Func<Task<Stream>>?>(() => Task.FromResult<Stream>(new NonClosingStream(destination)));
    }

    // Keeps the exported content readable after the exporter disposes its stream.
    private sealed class NonClosingStream(MemoryStream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    }
}
