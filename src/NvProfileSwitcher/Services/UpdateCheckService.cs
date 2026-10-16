using NvProfileSwitcher.Core;
using NvProfileSwitcher.Models;
using NvProfileSwitcher.Shell;

namespace NvProfileSwitcher.Services;

/// <summary>
/// Checks GitHub for a newer release, offers it and installs it. The automatic check runs
/// silently and only when enabled; the manual check reports every outcome. Development builds
/// never check. The release lookups and the installer are seams so the flow can be tested
/// without the network or processes; the application passes <see cref="UpdateService"/>. Unsaved
/// editor changes are confirmed through the editing service's single discard confirmation.
/// </summary>
internal sealed class UpdateCheckService(
    AppSettings settings,
    IDialogService dialogs,
    bool isDevelopmentBuild,
    Func<Task<UpdateReleaseInfo?>> getLatestReleaseInfo,
    Func<UpdateReleaseInfo, Task<UpdateRelease?>> getReleasePackage,
    IUpdateInstaller installer,
    Func<Task<bool>> confirmAndDiscardChanges,
    Func<bool> shutdown)
{
    // An installation is in progress; another one is not started meanwhile.
    private bool installing;

    private const string Title = "Check for updates";
    private const string CouldNotCheck = "Could not check for updates.\n\nPlease try again later.";

    public async Task CheckAsync(bool manual)
    {
        if (isDevelopmentBuild)
        {
            if (manual)
                await dialogs.ShowMessageAsync(Title, "Update checks are disabled in development builds.");
            return;
        }

        if (!manual && !settings.CheckForUpdates) return;

        try
        {
            var releaseInfo = await getLatestReleaseInfo();
            if (releaseInfo is null)
            {
                if (manual)
                    await dialogs.ShowMessageAsync(Title, CouldNotCheck);
                return;
            }

            if (!UpdateCore.IsVersionNewer(releaseInfo.Version, BuildInfo.Version))
            {
                if (manual)
                    await dialogs.ShowMessageAsync(Title,
                        $"NvProfileSwitcher is up to date.\n\nCurrent version: {BuildInfo.Version}");
                return;
            }

            var release = await getReleasePackage(releaseInfo);
            if (release is null)
            {
                if (manual)
                    await dialogs.ShowMessageAsync(Title, CouldNotCheck);
                return;
            }

            await dialogs.ShowUpdateAvailableAsync(release, progress => InstallAsync(release, progress));
        }
        catch
        {
            if (manual)
                await dialogs.ShowMessageAsync(Title, CouldNotCheck);
        }
    }

    /// <summary>
    /// Installs <paramref name="release"/> and closes the application for it; true once the
    /// application is closing. The update is downloaded and verified first, then unsaved editor
    /// changes are confirmed for discard, and only then is it launched and the application
    /// closed, with nothing in between that could let the user change anything again. False when
    /// the update was not launched or the application did not close, with the update cancelled and
    /// its files deleted: "Keep editing", another installation in progress, or a close that did
    /// not happen. A failure to prepare or launch it propagates; the installation is unchanged.
    /// (Deliberate deviation: the original launched the update before the close confirmation, and
    /// keeping the changes left the update dialog waiting forever.)
    /// </summary>
    public async Task<bool> InstallAsync(UpdateRelease release, IProgress<string> progress)
    {
        if (installing)
            return false;

        installing = true;
        try
        {
            var prepared = await installer.PrepareAsync(release, progress);
            if (!await confirmAndDiscardChanges())
            {
                installer.Discard(prepared);
                return false;
            }

            var launched = installer.Launch(prepared);
            progress.Report("Restarting NvProfileSwitcher...");
            if (shutdown())
                return true;

            installer.Cancel(launched);
            return false;
        }
        finally
        {
            installing = false;
        }
    }
}
