namespace NvProfileSwitcher.Core.Tests;

// GitHub release lookups without the network. Each lookup returns the configured value,
// throws the configured failure, or waits for Pending when it is set.
internal sealed class FakeReleaseSource
{
    public static readonly string NewerVersion = "999.0.0";

    public UpdateReleaseInfo? Info { get; set; } = new(NewerVersion, new Uri("https://example.test/release"));
    public UpdateRelease? Package { get; set; } = Release(NewerVersion);
    public Exception? InfoFailure { get; set; }
    public TaskCompletionSource<UpdateReleaseInfo?>? Pending { get; set; }
    public int InfoRequests { get; private set; }
    public int PackageRequests { get; private set; }

    public static UpdateRelease Release(string version) => new(
        version,
        new Uri("https://example.test/release"),
        new Uri("https://example.test/package.zip"),
        new Uri("https://example.test/package.zip.sha256"),
        "package.zip",
        "package.zip.sha256",
        IsInstaller: false);

    public Task<UpdateReleaseInfo?> GetLatestReleaseInfoAsync()
    {
        InfoRequests++;
        if (InfoFailure is not null)
            return Task.FromException<UpdateReleaseInfo?>(InfoFailure);
        return Pending?.Task ?? Task.FromResult(Info);
    }

    public Task<UpdateRelease?> GetReleasePackageAsync(UpdateReleaseInfo info)
    {
        PackageRequests++;
        return Task.FromResult(Package);
    }
}
