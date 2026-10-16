using Xunit;

namespace NvProfileSwitcher.Core.Tests;

// What updates leave in %TEMP%: the folders are deleted when the primary instance starts, and the
// relauncher of an installed update deletes the installer files before it starts the application.
public sealed class UpdateCleanupTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "nvps-update-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private string CreateUpdate(string name, params string[] files)
    {
        var folder = Path.Combine(root, name);
        foreach (var file in files)
        {
            var path = Path.Combine(folder, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, file);
        }
        return folder;
    }

    [Fact]
    public void LeftoverUpdatesAreDeleted()
    {
        CreateUpdate("installed", "NvProfileSwitcher-Setup-v3.0.1.exe", "NvProfileSwitcher-Setup-v3.0.1.exe.sha256",
            Path.Combine("relauncher", "NvProfileSwitcher.UpdateHelper.exe"));
        CreateUpdate("portable", Path.Combine("helper", "NvProfileSwitcher.UpdateHelper.exe"),
            Path.Combine("backup", "NvProfileSwitcher.exe"));

        UpdateService.DeleteLeftoverUpdates(root);

        Assert.True(Directory.Exists(root));
        Assert.Empty(Directory.EnumerateFileSystemEntries(root));
    }

    [Fact]
    public void AMissingUpdatesFolderIsNotAnError()
    {
        UpdateService.DeleteLeftoverUpdates(root);

        Assert.False(Directory.Exists(root));
    }

    public static TheoryData<string[]> InvalidRelauncherArguments => new()
    {
        new[] { "--relaunch-after-install" },
        new[] { "--relaunch-after-install", "pid", "app.exe", "update" },
        new[] { "--relaunch-after-install", "1", "", "update" },
        new[] { "--relaunch-after-install", "1", "app.exe", " " },
        new[] { "--relaunch-after-install", "1", "app.exe", "update", "extra" }
    };

    [Theory]
    [MemberData(nameof(InvalidRelauncherArguments))]
    public void TheRelauncherRejectsInvalidArguments(string[] args)
    {
        Assert.Equal(2, UpdateService.RelaunchAfterInstall(args));
    }

    // The installer already exited and the executable does not exist (the installation failed
    // before it): nothing is started, and the installer files are deleted while the
    // relauncher's own folder is kept for the next start.
    [Fact]
    public void WithoutTheExecutableTheRelauncherStartsNothingAndDeletesTheInstaller()
    {
        var update = CreateUpdate("installed", "NvProfileSwitcher-Setup-v3.0.1.exe", "NvProfileSwitcher-Setup-v3.0.1.exe.sha256",
            Path.Combine("relauncher", "NvProfileSwitcher.UpdateHelper.exe"));

        var result = UpdateService.RelaunchAfterInstall(
            [UpdateService.RelaunchAfterInstallArgument, int.MaxValue.ToString(), Path.Combine(root, "missing", "NvProfileSwitcher.exe"), update]);

        Assert.Equal(4, result);
        Assert.Empty(Directory.EnumerateFiles(update));
        Assert.True(File.Exists(Path.Combine(update, "relauncher", "NvProfileSwitcher.UpdateHelper.exe")));
    }
}
