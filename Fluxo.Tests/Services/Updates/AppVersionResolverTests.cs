using Fluxo.Services.Updates;
using Xunit;

namespace Fluxo.Tests.Services.Updates;

public sealed class AppVersionResolverTests
{
    [Fact]
    public void AppVersionResolver_ResolveCurrentVersion_ReturnsNonEmptyAndNotUnknown()
    {
        var version = AppVersionResolver.ResolveCurrentVersion();

        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.NotEqual("Unknown", version);
    }

    [Fact]
    public void AppVersionResolver_ResolveVersion_StripsInformationalMetadataSuffixAfterPlus()
    {
        var version = AppVersionResolver.ResolveVersion("1.2.3+abc123", new Version(9, 9, 9));

        Assert.Equal("1.2.3", version);
    }

    [Fact]
    public void AppVersionResolver_ResolveVersion_FallsBackToMajorMinorBuild()
    {
        var version = AppVersionResolver.ResolveVersion(null, new Version(2, 4, 6, 8));

        Assert.Equal("2.4.6", version);
    }

    [Fact]
    public void AppVersionResolver_ResolveVersion_ReturnsUnknown_WhenNoInformationalAndNoAssemblyVersion()
    {
        var version = AppVersionResolver.ResolveVersion(null, null);

        Assert.Equal("Unknown", version);
    }
}
