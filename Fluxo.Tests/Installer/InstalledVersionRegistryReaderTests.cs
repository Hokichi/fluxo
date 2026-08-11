using Fluxo.Installer.Services;
using Fluxo.Tests.TestSupport;
using Microsoft.Win32;
using Xunit;

namespace Fluxo.Tests.Installer;

public sealed class InstalledVersionRegistryReaderTests : IDisposable
{
    private readonly string testRootPath = $@"SOFTWARE\Fluxo.Tests\{Guid.NewGuid():N}";

    [Fact]
    public void InstalledVersionRegistryReader_ReadInstalledVersion_ReturnsInstalledVersionFromConfiguredFluxoKey()
    {
        using var testRoot = Registry.CurrentUser.CreateSubKey(testRootPath);
        using var fluxoKey = testRoot.CreateSubKey(@"Microsoft\Windows\CurrentVersion\fluxo");
        fluxoKey.SetValue("InstalledVersion", "1.2.3.4", RegistryValueKind.String);

        var version = InstalledVersionRegistryReader.ReadInstalledVersion(
            Registry.CurrentUser,
            $@"{testRootPath}\Microsoft\Windows\CurrentVersion\fluxo");

        Assert.Equal("1.2.3.4", version);
    }

    [Fact]
    public void InstalledVersionRegistryReader_ReadInstallLocation_ReturnsInstallLocationFromConfiguredFluxoKey()
    {
        using var testRoot = Registry.CurrentUser.CreateSubKey(testRootPath);
        using var fluxoKey = testRoot.CreateSubKey(@"Microsoft\Windows\CurrentVersion\fluxo");
        fluxoKey.SetValue("InstallLocation", WindowsPathFixtures.AppsFluxoFolder, RegistryValueKind.String);

        var installLocation = InstalledVersionRegistryReader.ReadInstallLocation(
            Registry.CurrentUser,
            $@"{testRootPath}\Microsoft\Windows\CurrentVersion\fluxo");

        Assert.Equal(WindowsPathFixtures.AppsFluxoFolder, installLocation);
    }

    [Fact]
    public void InstalledVersionRegistryReader_ReadInstalledVersion_IgnoresLegacyUninstallDisplayVersionEntries()
    {
        using var testRoot = Registry.CurrentUser.CreateSubKey(testRootPath);
        using var legacyUninstallKey = testRoot.CreateSubKey(
            @"Microsoft\Windows\CurrentVersion\Uninstall\{11111111-1111-1111-1111-111111111111}");
        legacyUninstallKey.SetValue("DisplayName", "fluxo", RegistryValueKind.String);
        legacyUninstallKey.SetValue("Publisher", "fluxo", RegistryValueKind.String);
        legacyUninstallKey.SetValue("DisplayVersion", "9.9.9.9", RegistryValueKind.String);

        var version = InstalledVersionRegistryReader.ReadInstalledVersion(
            Registry.CurrentUser,
            $@"{testRootPath}\Microsoft\Windows\CurrentVersion\fluxo");

        Assert.Null(version);
    }

    public void Dispose()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(testRootPath, throwOnMissingSubKey: false);
        }
        catch
        {
        }
    }
}
