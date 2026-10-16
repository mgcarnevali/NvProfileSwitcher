using NvProfileSwitcher.Models;
using NvProfileSwitcher.Services;
using Xunit;

namespace NvProfileSwitcher.Core.Tests;

// The update check flow of the original CheckForUpdatesAsync: the automatic check is silent
// and runs only when enabled, the manual check reports every outcome and development
// builds never check.
public sealed class UpdateCheckServiceTests
{
    private const string Title = "Check for updates";
    private const string CouldNotCheck = "Could not check for updates.\n\nPlease try again later.";

    private readonly AppSettings settings = new() { CheckForUpdates = true };
    private readonly FakeDialogService dialogs = new();
    private readonly FakeReleaseSource releases = new();

    private readonly FakeUpdateInstaller installer = new();
    private bool discardAnswer = true;
    private bool shutdownAnswer = true;
    private int discardRequests;
    private int shutdownRequests;

    private UpdateCheckService Create(bool isDevelopmentBuild = false) =>
        new(settings, dialogs, isDevelopmentBuild, releases.GetLatestReleaseInfoAsync, releases.GetReleasePackageAsync,
            installer,
            () =>
            {
                discardRequests++;
                installer.Log.Add("confirm");
                return Task.FromResult(discardAnswer);
            },
            () =>
            {
                shutdownRequests++;
                installer.Log.Add("shutdown");
                return shutdownAnswer;
            });

    private static List<(string Title, string Message)> ReportedOnlyIfManual(bool manual, string message) =>
        manual ? [(Title, message)] : [];

    [Fact]
    public async Task DevelopmentBuildManualCheckOnlyReportsThatChecksAreDisabled()
    {
        await Create(isDevelopmentBuild: true).CheckAsync(manual: true);

        Assert.Equal([(Title, "Update checks are disabled in development builds.")], dialogs.Messages);
        Assert.Equal(0, releases.InfoRequests);
        Assert.Empty(dialogs.UpdatesShown);
    }

    [Fact]
    public async Task DevelopmentBuildAutomaticCheckDoesNothingEvenWhenEnabled()
    {
        await Create(isDevelopmentBuild: true).CheckAsync(manual: false);

        Assert.Empty(dialogs.Messages);
        Assert.Equal(0, releases.InfoRequests);
    }

    [Fact]
    public async Task AutomaticCheckDoesNotQueryWhenDisabled()
    {
        settings.CheckForUpdates = false;

        await Create().CheckAsync(manual: false);

        Assert.Equal(0, releases.InfoRequests);
        Assert.Empty(dialogs.Messages);
    }

    [Fact]
    public async Task ManualCheckQueriesEvenWhenAutomaticChecksAreDisabled()
    {
        settings.CheckForUpdates = false;

        await Create().CheckAsync(manual: true);

        Assert.Equal(1, releases.InfoRequests);
        Assert.Single(dialogs.UpdatesShown);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NewerReleaseWithPackageShowsTheUpdateDialog(bool manual)
    {
        await Create().CheckAsync(manual);

        Assert.Same(releases.Package, Assert.Single(dialogs.UpdatesShown));
        Assert.Empty(dialogs.Messages);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingReleaseInfoIsReportedOnlyByTheManualCheck(bool manual)
    {
        releases.Info = null;

        await Create().CheckAsync(manual);

        Assert.Equal(ReportedOnlyIfManual(manual, CouldNotCheck), dialogs.Messages);
        Assert.Equal(0, releases.PackageRequests);
        Assert.Empty(dialogs.UpdatesShown);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CurrentVersionIsUpToDateWithoutRequestingThePackage(bool manual)
    {
        releases.Info = new UpdateReleaseInfo(BuildInfo.Version, new Uri("https://example.test/release"));

        await Create().CheckAsync(manual);

        Assert.Equal(
            ReportedOnlyIfManual(manual, $"NvProfileSwitcher is up to date.\n\nCurrent version: {BuildInfo.Version}"),
            dialogs.Messages);
        Assert.Equal(0, releases.PackageRequests);
        Assert.Empty(dialogs.UpdatesShown);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingPackageIsReportedOnlyByTheManualCheck(bool manual)
    {
        releases.Package = null;

        await Create().CheckAsync(manual);

        Assert.Equal(ReportedOnlyIfManual(manual, CouldNotCheck), dialogs.Messages);
        Assert.Equal(1, releases.PackageRequests);
        Assert.Empty(dialogs.UpdatesShown);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LookupFailureIsReportedOnlyByTheManualCheck(bool manual)
    {
        releases.InfoFailure = new HttpRequestException("offline");

        await Create().CheckAsync(manual);

        Assert.Equal(ReportedOnlyIfManual(manual, CouldNotCheck), dialogs.Messages);
        Assert.Empty(dialogs.UpdatesShown);
    }

    // As in the original, showing the update dialog is inside the same try as the lookups.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpdateDialogFailureIsReportedOnlyByTheManualCheck(bool manual)
    {
        dialogs.UpdateDialogFailure = new InvalidOperationException("dialog");

        await Create().CheckAsync(manual);

        Assert.Equal(ReportedOnlyIfManual(manual, CouldNotCheck), dialogs.Messages);
    }

    // The setting is read when each check starts, not when the service is created.
    [Fact]
    public async Task AutomaticCheckReadsTheCurrentSetting()
    {
        settings.CheckForUpdates = false;
        var service = Create();
        settings.CheckForUpdates = true;

        await service.CheckAsync(manual: false);

        Assert.Equal(1, releases.InfoRequests);
    }

    private static readonly IProgress<string> NoProgress = new Progress<string>();

    // The update is prepared first, unsaved changes are confirmed for discard, and only then is it
    // launched and the application closed.
    [Fact]
    public async Task InstallingPreparesConfirmsLaunchesAndCloses()
    {
        var release = FakeReleaseSource.Release("2.0.0");

        Assert.True(await Create().InstallAsync(release, NoProgress));

        Assert.Equal(["prepare 2.0.0", "confirm", "launch root 2.0.0", "shutdown"], installer.Log);
    }

    // "Keep editing" cancels the update before it is launched; its files are deleted.
    [Fact]
    public async Task KeepingTheChangesCancelsTheUpdate()
    {
        discardAnswer = false;

        Assert.False(await Create().InstallAsync(FakeReleaseSource.Release("2.0.0"), NoProgress));

        Assert.Equal(["prepare 2.0.0", "confirm", "discard root 2.0.0"], installer.Log);
        Assert.Equal(0, shutdownRequests);
    }

    // When the application does not close after the launch, the launched update is cancelled.
    [Fact]
    public async Task AnApplicationThatDoesNotCloseCancelsTheLaunchedUpdate()
    {
        shutdownAnswer = false;

        Assert.False(await Create().InstallAsync(FakeReleaseSource.Release("2.0.0"), NoProgress));

        Assert.Equal(["prepare 2.0.0", "confirm", "launch root 2.0.0", "shutdown", "cancel root 2.0.0"], installer.Log);
    }

    // A failure to prepare or launch propagates, before anything is confirmed or after nothing
    // was started, and the next installation can run.
    [Fact]
    public async Task FailuresPropagateAndLeaveTheFlowReady()
    {
        var service = Create();
        installer.PrepareFailure = new InvalidOperationException("download");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.InstallAsync(FakeReleaseSource.Release("2.0.0"), NoProgress));
        Assert.Equal(0, discardRequests);

        installer.PrepareFailure = null;
        installer.LaunchFailure = new InvalidOperationException("launch");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.InstallAsync(FakeReleaseSource.Release("2.0.0"), NoProgress));
        Assert.Equal(0, shutdownRequests);

        installer.LaunchFailure = null;
        Assert.True(await service.InstallAsync(FakeReleaseSource.Release("2.0.0"), NoProgress));
    }

    // While one installation runs, another one (from a second update dialog) does nothing.
    [Fact]
    public async Task OnlyOneInstallationRunsAtATime()
    {
        var service = Create();
        installer.PendingPreparation = new TaskCompletionSource();
        var first = service.InstallAsync(FakeReleaseSource.Release("2.0.0"), NoProgress);

        Assert.False(await service.InstallAsync(FakeReleaseSource.Release("2.0.0"), NoProgress));
        installer.PendingPreparation.SetResult();
        Assert.True(await first);

        Assert.Equal(["prepare 2.0.0", "confirm", "launch root 2.0.0", "shutdown"], installer.Log);
    }

    // The update dialog installs the release it offers.
    [Fact]
    public async Task TheOfferedReleaseIsTheOneInstalled()
    {
        var service = Create();
        await service.CheckAsync(manual: false);

        Assert.True(await Assert.Single(dialogs.UpdateInstalls)(NoProgress));

        Assert.Equal($"prepare {FakeReleaseSource.NewerVersion}", installer.Log[0]);
    }
}
