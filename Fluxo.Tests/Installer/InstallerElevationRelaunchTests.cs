using Fluxo.Installer.Models;
using Fluxo.Tests.TestSupport;
using Xunit;

namespace Fluxo.Tests.Installer;

public sealed class InstallerElevationRelaunchTests : IDisposable
{
    private readonly string bundlePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.exe");

    public InstallerElevationRelaunchTests()
    {
        File.WriteAllText(bundlePath, string.Empty);
    }

    [Fact]
    public void InstallerElevationRelaunch_ShouldRelaunch_InteractiveUnelevatedBundle()
    {
        var shouldRelaunch = InstallerElevationRelaunch.ShouldRelaunch(
            isInteractive: true,
            isElevated: false,
            originalBundlePath: bundlePath,
            currentProcessPath: WindowsPathFixtures.ExtractedBootstrapperProcess);

        Assert.True(shouldRelaunch);
    }

    [Fact]
    public void InstallerElevationRelaunch_ShouldRelaunchForElevation_RequiresInteractiveUnelevatedExistingBundle()
    {
        Assert.True(InstallerElevationRelaunch.ShouldRelaunchForElevation(
            isInteractive: true,
            isElevated: false,
            bundlePath));

        Assert.False(InstallerElevationRelaunch.ShouldRelaunchForElevation(
            isInteractive: false,
            isElevated: false,
            bundlePath));
        Assert.False(InstallerElevationRelaunch.ShouldRelaunchForElevation(
            isInteractive: true,
            isElevated: true,
            bundlePath));
        Assert.False(InstallerElevationRelaunch.ShouldRelaunchForElevation(
            isInteractive: true,
            isElevated: false,
            bundlePath: Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-missing.exe")));
    }

    [Fact]
    public void InstallerElevationRelaunch_SelectBundlePathForElevationRelaunch_PrefersRepairerSourceProcess_WhenOriginalIsInstaller()
    {
        var path = InstallerElevationRelaunch.SelectBundlePathForElevationRelaunch(
            wixBundleSourceProcessPath: WindowsPathFixtures.AlternateRepairerExecutable,
            wixBundleOriginalSource: WindowsPathFixtures.DownloadsInstaller,
            processPath: WindowsPathFixtures.ExtractedBundleProcess);

        Assert.Equal(WindowsPathFixtures.AlternateRepairerExecutable, path);
    }

    [Fact]
    public void InstallerElevationRelaunch_ShouldRelaunch_SkipsHeadlessOrAlreadyElevatedRuns()
    {
        Assert.False(InstallerElevationRelaunch.ShouldRelaunch(
            isInteractive: false,
            isElevated: false,
            originalBundlePath: bundlePath,
            currentProcessPath: WindowsPathFixtures.TempFile("Fluxo.Installer.exe")));
        Assert.False(InstallerElevationRelaunch.ShouldRelaunch(
            isInteractive: true,
            isElevated: true,
            originalBundlePath: bundlePath,
            currentProcessPath: WindowsPathFixtures.TempFile("Fluxo.Installer.exe")));
    }

    [Fact]
    public void InstallerElevationRelaunch_ShouldRelaunch_SkipsWhenOriginalPathIsCurrentProcess()
    {
        Assert.False(InstallerElevationRelaunch.ShouldRelaunch(
            isInteractive: true,
            isElevated: false,
            originalBundlePath: bundlePath,
            currentProcessPath: bundlePath));
    }

    [Fact]
    public void InstallerElevationRelaunch_CreateStartInfo_UsesRunAsShellVerb()
    {
        var startInfo = InstallerElevationRelaunch.CreateStartInfo(bundlePath);

        Assert.Equal(bundlePath, startInfo.FileName);
        Assert.True(startInfo.UseShellExecute);
        Assert.Equal("runas", startInfo.Verb);
    }

    public void Dispose()
    {
        File.Delete(bundlePath);
    }
}
