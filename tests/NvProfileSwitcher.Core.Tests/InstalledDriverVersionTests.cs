using NvProfileSwitcher.Platform;
using Xunit;

namespace NvProfileSwitcher.Core.Tests;

// The registry is the source of the installed driver version; the NVIDIA API library, which this
// process keeps loaded and so may report the driver it started with, is only the fallback.
public sealed class InstalledDriverVersionTests
{
    [Fact]
    public void TheRegistryVersionWinsAndTheLibraryIsNotRead()
    {
        var libraryReads = 0;

        var version = InstalledDriverVersion.Read(() => "621.01", () => { libraryReads++; return "617.42"; });

        Assert.Equal("621.01", version);
        Assert.Equal(0, libraryReads);
    }

    [Fact]
    public void WithoutARegistryVersionTheLibraryVersionIsUsed() =>
        Assert.Equal("617.42", InstalledDriverVersion.Read(() => null, () => "617.42"));

    [Fact]
    public void WithoutEitherThereIsNone() =>
        Assert.Null(InstalledDriverVersion.Read(() => null, () => null));
}
