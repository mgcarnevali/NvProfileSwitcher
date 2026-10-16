using System.Diagnostics;
using System.Runtime.InteropServices;
using NvProfileSwitcher.Nvidia;
using Xunit;

namespace NvProfileSwitcher.Core.Tests;

// A failed NVAPI initialization must release the module it loaded. The tests load a private
// copy of a system library without the NVAPI exports, under a unique path, so the module
// cannot already be loaded by anything else in the process.
public sealed class NvApiTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "NvProfileSwitcher.Tests", Guid.NewGuid().ToString("N"));

    public NvApiTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        try { Directory.Delete(directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void MissingLibraryFailsWithoutLoadingAnything()
    {
        var missing = Path.Combine(directory, "nvapi64.dll");

        Assert.Throws<DllNotFoundException>(() => new NvApi(missing));
        Assert.False(LoadedModules.IsLoaded(missing));
    }

    [Fact]
    public void FailedInitializationReleasesTheLoadedModule()
    {
        var library = LoadedModules.CopySystemLibrary(directory);

        // Control: the check detects the private copy while it is loaded.
        Assert.False(LoadedModules.IsLoaded(library));
        var handle = NativeLibrary.Load(library);
        Assert.True(LoadedModules.IsLoaded(library));
        NativeLibrary.Free(handle);
        Assert.False(LoadedModules.IsLoaded(library));

        Assert.Throws<EntryPointNotFoundException>(() => new NvApi(library));

        Assert.False(LoadedModules.IsLoaded(library));
    }
}

// Whether a library file is loaded in the current process, on Windows and Linux.
internal static class LoadedModules
{
    public static string CopySystemLibrary(string directory)
    {
        string source;
        if (OperatingSystem.IsWindows())
        {
            source = Path.Combine(Environment.SystemDirectory, "version.dll");
        }
        else if (OperatingSystem.IsLinux())
        {
            // The math library is always mapped by the runtime; copy the mapped file.
            source = File.ReadLines("/proc/self/maps")
                .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault())
                .First(path => path is not null && Path.GetFileName(path).StartsWith("libm.so", StringComparison.Ordinal))!;
        }
        else
        {
            Assert.Skip("Module loading is verified on Windows and Linux.");
            throw new UnreachableException();
        }

        var copy = Path.Combine(directory, $"nvps-test-{Guid.NewGuid():N}{Path.GetExtension(source)}");
        File.Copy(source, copy);
        return copy;
    }

    public static bool IsLoaded(string path)
    {
        if (OperatingSystem.IsWindows())
            return GetModuleHandleW(path) != IntPtr.Zero;

        var fullPath = Path.GetFullPath(path);
        return File.ReadLines("/proc/self/maps").Any(line => line.EndsWith(" " + fullPath, StringComparison.Ordinal));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string moduleName);
}
