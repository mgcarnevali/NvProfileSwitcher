using Xunit;

namespace NvProfileSwitcher.Core.Tests;

public sealed class UpdateCoreTests
{
    [Theory]
    [InlineData("2.2.0", "2.1.1", true)]
    [InlineData("v2.2.0", "2.1.1", true)]
    [InlineData("2.2.0", "2.2.0", false)]
    [InlineData("2.1.9", "2.2.0", false)]
    [InlineData("2.2.1", "2.2", true)]
    [InlineData("2.2", "2.2.0", false)]
    [InlineData("2.2.0-beta", "2.1.9", true)]
    [InlineData("invalid", "2.1.9", false)]
    public void IsVersionNewer_MatchesUpdaterRules(string remote, string local, bool expected)
    {
        Assert.Equal(expected, UpdateCore.IsVersionNewer(remote, local));
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef  NvProfileSwitcher-v3.1.0.zip", "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF")]
    public void ParseSha256_AcceptsStandardChecksumFormats(string value, string expected)
    {
        Assert.Equal(expected, UpdateCore.ParseSha256(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("zz23456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    public void ParseSha256_RejectsInvalidChecksums(string value)
    {
        Assert.Null(UpdateCore.ParseSha256(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("v")]
    [InlineData("2..1")]
    [InlineData("2.a.1")]
    public void ParseVersionParts_RejectsInvalidVersions(string version)
    {
        Assert.Null(UpdateCore.ParseVersionParts(version));
    }
}
