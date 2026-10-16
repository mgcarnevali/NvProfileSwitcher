using System.Diagnostics;
using System.IO.Compression;
using Microsoft.Win32;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace NvProfileSwitcher;

internal sealed record UpdateReleaseInfo(
    string Version,
    Uri Url);

internal sealed record UpdateRelease(
    string Version,
    Uri Url,
    Uri PackageUrl,
    Uri ChecksumUrl,
    string PackageName,
    string ChecksumName,
    bool IsInstaller);

/// <summary>A downloaded and verified update, staged under <paramref name="Root"/> and ready to be launched.</summary>
internal sealed record PreparedUpdate(bool IsInstaller, string Root, string FileName, string Arguments, string WorkingDirectory);

/// <summary>A prepared update whose helper or installer was started.</summary>
internal sealed record LaunchedUpdate(PreparedUpdate Update, Process? Process);

internal static class UpdateService
{
    private static readonly Uri LatestReleaseApi =
        new("https://api.github.com/repos/mgcarnevali/NvProfileSwitcher/releases/latest");

    internal static async Task<UpdateReleaseInfo?> GetLatestReleaseInfoAsync(
        CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync(LatestReleaseApi, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!json.RootElement.TryGetProperty("tag_name", out var tagElement) ||
            !json.RootElement.TryGetProperty("html_url", out var urlElement))
            return null;

        var tag = tagElement.GetString();
        var url = urlElement.GetString();
        if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var releaseUri))
            return null;

        var version = tag.Trim();
        if (version.Length > 0 && (version[0] == 'v' || version[0] == 'V')) version = version[1..];

        return new UpdateReleaseInfo(version, releaseUri);
    }

    internal static async Task<UpdateRelease?> GetReleasePackageAsync(
        UpdateReleaseInfo releaseInfo,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync(LatestReleaseApi, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!json.RootElement.TryGetProperty("tag_name", out var tagElement) ||
            !json.RootElement.TryGetProperty("html_url", out var urlElement) ||
            !json.RootElement.TryGetProperty("assets", out var assetsElement) ||
            assetsElement.ValueKind != JsonValueKind.Array)
            return null;

        var tag = tagElement.GetString();
        var url = urlElement.GetString();
        if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var releaseUri))
            return null;

        var version = tag.Trim();
        if (version.Length > 0 && (version[0] == 'v' || version[0] == 'V')) version = version[1..];

        // The latest release could change between the metadata check and package lookup.
        // Never resolve assets for a different release than the one the UI approved.
        if (!string.Equals(version, releaseInfo.Version, StringComparison.OrdinalIgnoreCase))
            return null;

        var isInstaller = IsCurrentApplicationInstalled();
        var packageName = isInstaller
            ? $"NvProfileSwitcher-Setup-v{version}.exe"
            : $"NvProfileSwitcher-v{version}.zip";
        var checksumName = packageName + ".sha256";
        Uri? packageUri = null;
        Uri? checksumUri = null;

        foreach (var asset in assetsElement.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var nameElement) ||
                !asset.TryGetProperty("browser_download_url", out var downloadElement))
                continue;

            var name = nameElement.GetString();
            var downloadUrl = downloadElement.GetString();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(downloadUrl) ||
                !Uri.TryCreate(downloadUrl, UriKind.Absolute, out var downloadUri))
                continue;

            if (string.Equals(name, packageName, StringComparison.OrdinalIgnoreCase))
                packageUri = downloadUri;
            else if (string.Equals(name, checksumName, StringComparison.OrdinalIgnoreCase))
                checksumUri = downloadUri;
        }

        if (packageUri is null || checksumUri is null)
            return null;

        return new UpdateRelease(
            version, releaseUri, packageUri, checksumUri, packageName, checksumName, isInstaller);
    }

    /// <summary>
    /// Downloads and verifies the update and stages it to be launched: the installer, or the
    /// extracted package and a copy of this executable as the helper that applies it. Nothing in
    /// the installation changes. On failure the staged files are deleted and the error propagates.
    /// </summary>
    internal static async Task<PreparedUpdate> PrepareAsync(
        UpdateRelease release,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
            throw new InvalidOperationException("Could not locate the running NvProfileSwitcher executable.");

        var installDirectory = Path.GetDirectoryName(processPath)
            ?? throw new InvalidOperationException("Could not determine the application directory.");

        if (release.IsInstaller)
            return await PrepareInstallerUpdateAsync(release, progress, cancellationToken);

        EnsureInstallDirectoryWritable(installDirectory);

        var updateRoot = Path.Combine(Path.GetTempPath(), "NvProfileSwitcher", "Updates", Guid.NewGuid().ToString("N"));
        var downloadDirectory = Path.Combine(updateRoot, "download");
        var extractDirectory = Path.Combine(updateRoot, "extract");
        var helperDirectory = Path.Combine(updateRoot, "helper");
        Directory.CreateDirectory(downloadDirectory);
        Directory.CreateDirectory(extractDirectory);
        Directory.CreateDirectory(helperDirectory);

        var packagePath = Path.Combine(downloadDirectory, release.PackageName);
        var checksumPath = Path.Combine(downloadDirectory, release.ChecksumName);

        try
        {
            progress?.Report("Downloading update...");
            using var client = CreateClient(TimeSpan.FromMinutes(5));
            await DownloadFileAsync(client, release.PackageUrl, packagePath, cancellationToken);
            await DownloadFileAsync(client, release.ChecksumUrl, checksumPath, cancellationToken);

            progress?.Report("Verifying update...");
            var expectedHash = UpdateCore.ParseSha256(await File.ReadAllTextAsync(checksumPath, cancellationToken));
            if (expectedHash is null)
                throw new InvalidDataException("The update checksum file is invalid.");

            await using (var packageStream = File.OpenRead(packagePath))
            {
                var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(packageStream, cancellationToken));
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The downloaded update failed SHA-256 verification.");
            }

            progress?.Report("Preparing update...");
            ZipFile.ExtractToDirectory(packagePath, extractDirectory);
            var stagedDirectory = ResolveStagedApplicationDirectory(extractDirectory);
            var stagedExecutable = Path.Combine(stagedDirectory, "NvProfileSwitcher.exe");
            if (!File.Exists(stagedExecutable))
                throw new InvalidDataException("The update package does not contain NvProfileSwitcher.exe.");

            var helperPath = Path.Combine(helperDirectory, "NvProfileSwitcher.UpdateHelper.exe");
            File.Copy(processPath, helperPath, overwrite: true);

            var arguments = string.Join(' ',
                "--apply-update",
                Process.GetCurrentProcess().Id.ToString(),
                Quote(stagedDirectory),
                Quote(installDirectory),
                Quote(processPath),
                Quote(updateRoot));
            return new PreparedUpdate(IsInstaller: false, updateRoot, helperPath, arguments, helperDirectory);
        }
        catch
        {
            TryDeleteDirectory(updateRoot);
            throw;
        }
    }

    /// <summary>
    /// Starts the prepared update: the helper, which waits for this process to exit before it
    /// changes any file, or the installer. On failure the staged files are deleted and the error
    /// propagates.
    /// </summary>
    internal static LaunchedUpdate Launch(PreparedUpdate update)
    {
        try
        {
            var process = update.IsInstaller
                ? Process.Start(new ProcessStartInfo(update.FileName, update.Arguments)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = update.WorkingDirectory
                })
                : Process.Start(new ProcessStartInfo(update.FileName, update.Arguments)
                {
                    UseShellExecute = false,
                    WorkingDirectory = update.WorkingDirectory
                });

            return process is null
                ? throw new InvalidOperationException(update.IsInstaller
                    ? "Could not start the update installer."
                    : "Could not start the update helper.")
                : new LaunchedUpdate(update, process);
        }
        catch
        {
            TryDeleteDirectory(update.Root);
            throw;
        }
    }

    /// <summary>
    /// Stops a launched portable update because this process keeps running: the helper is still
    /// waiting for this process to exit, so it has not changed any file yet, and ending it is
    /// safe. The staged files are deleted. A launched installer is left alone: it closes this
    /// application itself, and it may already be changing the installation.
    /// </summary>
    internal static void Cancel(LaunchedUpdate launched)
    {
        if (launched.Update.IsInstaller)
            return;

        try
        {
            if (launched.Process is { HasExited: false } helper)
            {
                helper.Kill();
                helper.WaitForExit(5000);
            }
        }
        catch
        {
            // The helper exited on its own meanwhile.
        }

        TryDeleteDirectory(launched.Update.Root);
    }

    /// <summary>Deletes a prepared update that is not launched.</summary>
    internal static void Discard(PreparedUpdate update) => TryDeleteDirectory(update.Root);

    private static async Task<PreparedUpdate> PrepareInstallerUpdateAsync(
        UpdateRelease release,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var updateRoot = Path.Combine(Path.GetTempPath(), "NvProfileSwitcher", "Updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(updateRoot);

        var installerPath = Path.Combine(updateRoot, release.PackageName);
        var checksumPath = Path.Combine(updateRoot, release.ChecksumName);

        try
        {
            progress?.Report("Downloading update...");
            using var client = CreateClient(TimeSpan.FromMinutes(5));
            await DownloadFileAsync(client, release.PackageUrl, installerPath, cancellationToken);
            await DownloadFileAsync(client, release.ChecksumUrl, checksumPath, cancellationToken);

            progress?.Report("Verifying update...");
            var expectedHash = UpdateCore.ParseSha256(await File.ReadAllTextAsync(checksumPath, cancellationToken));
            if (expectedHash is null)
                throw new InvalidDataException("The installer checksum file is invalid.");

            await using (var installerStream = File.OpenRead(installerPath))
            {
                var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(installerStream, cancellationToken));
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The downloaded installer failed SHA-256 verification.");
            }

            progress?.Report("Starting installer...");
            return new PreparedUpdate(IsInstaller: true, updateRoot, installerPath, "/SILENT /CLOSEAPPLICATIONS /NORESTART", updateRoot);
        }
        catch
        {
            TryDeleteDirectory(updateRoot);
            throw;
        }
    }

    private static bool IsCurrentApplicationInstalled()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
            return false;

        var applicationDirectory = Path.GetDirectoryName(processPath);
        return !string.IsNullOrWhiteSpace(applicationDirectory) && IsInstalledApplication(applicationDirectory);
    }

    private static bool IsInstalledApplication(string applicationDirectory)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null) return false;

            foreach (var subKeyName in uninstall.GetSubKeyNames())
            {
                using var subKey = uninstall.OpenSubKey(subKeyName);
                if (subKey is null)
                    continue;

                var displayName = subKey.GetValue("DisplayName") as string;
                if (string.IsNullOrWhiteSpace(displayName) ||
                    !displayName.StartsWith("NvProfileSwitcher", StringComparison.OrdinalIgnoreCase))
                    continue;

                var installLocation = subKey.GetValue("InstallLocation") as string;
                if (string.IsNullOrWhiteSpace(installLocation))
                    continue;

                if (PathsEqual(installLocation, applicationDirectory))
                    return true;
            }
        }
        catch
        {
            // If installation metadata cannot be read, fall back to portable behavior.
        }

        return false;
    }

    private static bool PathsEqual(string left, string right)
    {
        var a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(left));
        var b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(right));
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    internal static int ApplyPreparedUpdate(string[] args)
    {
        if (args.Length != 6 || !int.TryParse(args[1], out var parentProcessId)) return 2;

        var stagedDirectory = args[2];
        var installDirectory = args[3];
        var targetExecutable = args[4];
        var updateRoot = args[5];
        var backupDirectory = Path.Combine(updateRoot, "backup");
        var replacedFiles = new List<(string Destination, string? Backup)>();

        try
        {
            try
            {
                using var parent = Process.GetProcessById(parentProcessId);
                parent.WaitForExit(30_000);
                if (!parent.HasExited) return 3;
            }
            catch (ArgumentException)
            {
                // The application already exited.
            }

            Directory.CreateDirectory(backupDirectory);
            foreach (var source in Directory.EnumerateFiles(stagedDirectory, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(stagedDirectory, source);
                var destination = Path.Combine(installDirectory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                string? backup = null;
                if (File.Exists(destination))
                {
                    backup = Path.Combine(backupDirectory, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Copy(destination, backup, overwrite: true);
                }

                File.Copy(source, destination, overwrite: true);
                replacedFiles.Add((destination, backup));
            }

            Process.Start(new ProcessStartInfo(targetExecutable) { UseShellExecute = true });
            return 0;
        }
        catch
        {
            for (var i = replacedFiles.Count - 1; i >= 0; i--)
            {
                var (destination, backup) = replacedFiles[i];
                try
                {
                    if (backup is not null && File.Exists(backup))
                        File.Copy(backup, destination, overwrite: true);
                    else if (File.Exists(destination))
                        File.Delete(destination);
                }
                catch
                {
                    // Best-effort rollback. Preserve the original failure exit code.
                }
            }

            try
            {
                if (File.Exists(targetExecutable))
                    Process.Start(new ProcessStartInfo(targetExecutable) { UseShellExecute = true });
            }
            catch
            {
                // Nothing else can be recovered from the helper process.
            }

            return 4;
        }
    }

    private static HttpClient CreateClient(TimeSpan? timeout = null)
    {
        var client = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NvProfileSwitcher");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    private static async Task DownloadFileAsync(HttpClient client, Uri uri, string destination, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await source.CopyToAsync(target, cancellationToken);
    }

    private static string ResolveStagedApplicationDirectory(string extractDirectory)
    {
        var directExecutable = Path.Combine(extractDirectory, "NvProfileSwitcher.exe");
        if (File.Exists(directExecutable)) return extractDirectory;

        var nestedDirectory = Path.Combine(extractDirectory, "NvProfileSwitcher");
        if (File.Exists(Path.Combine(nestedDirectory, "NvProfileSwitcher.exe"))) return nestedDirectory;

        throw new InvalidDataException("The update package layout is invalid.");
    }

    private static void EnsureInstallDirectoryWritable(string installDirectory)
    {
        var probe = Path.Combine(installDirectory, $".nvps-update-{Guid.NewGuid():N}.tmp");
        try
        {
            using (File.Create(probe)) { }
        }
        catch (Exception ex)
        {
            throw new UnauthorizedAccessException("NvProfileSwitcher cannot update files in its current folder.", ex);
        }
        finally
        {
            try { File.Delete(probe); } catch { }
        }
    }

    private static string Quote(string value) => '"' + value.Replace("\"", "\\\"") + '"';

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }
}
