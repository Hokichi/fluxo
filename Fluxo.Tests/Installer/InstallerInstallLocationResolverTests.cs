using Fluxo.Installer.Models;
using Xunit;

namespace Fluxo.Tests.Installer;

public sealed class InstallerInstallLocationResolverTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative")]
    [InlineData(@"C:\")]
    [InlineData(@"C:\gone")]
    [InlineData("C:\\bad\0path")]
    public void InvalidFirstRegistration_UsesValidSecondRegistration(string? first)
    {
        var result = InstallerInstallLocationResolver.Resolve(
            [first, @"D:\My Apps\fluxo\"], @"E:\New", @"C:\Program Files\fluxo",
            path => path == @"D:\My Apps\fluxo");
        Assert.Equal(@"D:\My Apps\fluxo", result);
    }

    [Fact]
    public void InaccessibleRegistration_UsesRequestedFreshInstallFolder()
    {
        var result = InstallerInstallLocationResolver.Resolve(
            [@"D:\fluxo"], @"E:\New", @"C:\Program Files\fluxo",
            _ => throw new UnauthorizedAccessException());
        Assert.Equal(@"E:\New", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("relative")]
    [InlineData(@"C:\")]
    public void NoValidLocation_UsesDefault(string? requested)
    {
        Assert.Equal(@"C:\Program Files\fluxo", InstallerInstallLocationResolver.Resolve(
            [], requested, @"C:\Program Files\fluxo", _ => false));
    }
}
