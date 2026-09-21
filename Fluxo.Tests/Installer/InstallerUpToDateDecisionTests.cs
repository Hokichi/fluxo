using Fluxo.Installer.Models;
using Xunit;

namespace Fluxo.Tests.Installer;

public sealed class InstallerUpToDateDecisionTests
{
    [Theory]
    [InlineData("1.0.0.0", "1.0.0.0", 0)]
    [InlineData("1.0.0.0", "1.1.0.0", 1)]
    public void InstallerUpToDateDecision_Install_WhenDetectedVersionIsSameOrHigherShouldSkipAndCompareUsesInstalledThenCurrent(
        string currentBundleVersion,
        string highestDetectedInstalledVersion,
        int compareResult)
    {
        string? actualLeft = null;
        string? actualRight = null;

        var skip = InstallerUpToDateDecision.ShouldSkipInstall(
            InstallerOperationMode.Install,
            detectStatus: 0,
            currentBundleVersion: currentBundleVersion,
            highestDetectedInstalledVersion: highestDetectedInstalledVersion,
            compareVersions: (left, right) =>
            {
                actualLeft = left;
                actualRight = right;
                return compareResult;
            });

        Assert.True(skip);
        Assert.Equal(highestDetectedInstalledVersion, actualLeft);
        Assert.Equal(currentBundleVersion, actualRight);
    }

    [Theory]
    [InlineData(InstallerOperationMode.Maintenance, "1.0.0.0")]
    [InlineData(InstallerOperationMode.Uninstall, "2.0.0.0")]
    public void InstallerUpToDateDecision_MaintenanceOrUninstall_ShouldNotSkip(
        InstallerOperationMode operationMode,
        string highestDetectedInstalledVersion)
    {
        var compareCalled = false;
        var skip = InstallerUpToDateDecision.ShouldSkipInstall(
            operationMode,
            detectStatus: 0,
            currentBundleVersion: "1.0.0.0",
            highestDetectedInstalledVersion: highestDetectedInstalledVersion,
            compareVersions: (_, _) =>
            {
                compareCalled = true;
                return 1;
            });

        Assert.False(skip);
        Assert.False(compareCalled);
    }

    [Fact]
    public void InstallerUpToDateDecision_Install_WhenInstalledExecutableVersionIsHigher_ShouldSkip()
    {
        var decision = InstallerUpToDateDecision.Evaluate(
            InstallerOperationMode.Install,
            detectStatus: 0,
            currentBundleVersion: "1.0.0.0",
            highestDetectedInstalledVersion: null,
            registryInstalledVersion: null,
            installedExecutableVersion: "1.1.0.0",
            compareVersions: CompareVersions);

        Assert.True(decision.ShouldSkipInstall);
        Assert.True(decision.IsNewerVersion);
        Assert.Equal("1.1.0.0", decision.InstalledVersion);
    }

    [Fact]
    public void InstallerUpToDateDecision_Install_WhenInstalledExecutableVersionIsLower_ShouldNotSkip()
    {
        var skip = InstallerUpToDateDecision.ShouldSkipInstall(
            InstallerOperationMode.Install,
            detectStatus: 0,
            currentBundleVersion: "1.1.0.0",
            highestDetectedInstalledVersion: null,
            installedExecutableVersion: "1.0.0.0",
            compareVersions: CompareVersions);

        Assert.False(skip);
    }

    [Fact]
    public void InstallerUpToDateDecision_Install_WhenBundleAndExecutableVersionsExist_ShouldCompareHighestDetectedCandidate()
    {
        string? comparedInstalledVersion = null;

        var skip = InstallerUpToDateDecision.ShouldSkipInstall(
            InstallerOperationMode.Install,
            detectStatus: 0,
            currentBundleVersion: "1.0.0.0",
            highestDetectedInstalledVersion: "1.1.0.0",
            registryInstalledVersion: null,
            installedExecutableVersion: "1.2.0.0",
            compareVersions: (left, right) =>
            {
                if (right == "1.0.0.0")
                {
                    comparedInstalledVersion = left;
                }

                return CompareVersions(left, right);
            });

        Assert.True(skip);
        Assert.Equal("1.2.0.0", comparedInstalledVersion);
    }

    [Fact]
    public void InstallerUpToDateDecision_Install_WhenRegistryVersionIsHigher_ShouldSkipAndReportRegistryVersion()
    {
        var decision = InstallerUpToDateDecision.Evaluate(
            InstallerOperationMode.Install,
            detectStatus: 0,
            currentBundleVersion: "1.0.0.0",
            highestDetectedInstalledVersion: null,
            registryInstalledVersion: "1.3.0.0",
            installedExecutableVersion: null,
            compareVersions: CompareVersions);

        Assert.True(decision.ShouldSkipInstall);
        Assert.True(decision.IsNewerVersion);
        Assert.Equal("1.3.0.0", decision.InstalledVersion);
    }

    [Fact]
    public void InstallerUpToDateDecision_Install_WhenRegistryAndExecutableVersionsExist_ShouldReportHighestCandidate()
    {
        var decision = InstallerUpToDateDecision.Evaluate(
            InstallerOperationMode.Install,
            detectStatus: 0,
            currentBundleVersion: "1.0.0.0",
            highestDetectedInstalledVersion: null,
            registryInstalledVersion: "1.4.0.0",
            installedExecutableVersion: "1.3.0.0",
            compareVersions: CompareVersions);

        Assert.True(decision.ShouldSkipInstall);
        Assert.True(decision.IsNewerVersion);
        Assert.Equal("1.4.0.0", decision.InstalledVersion);
    }

    [Fact]
    public void InstallerUpToDateDecision_Install_WhenInstalledExecutableVersionIsSame_ShouldSkipWithoutNewerFlag()
    {
        var decision = InstallerUpToDateDecision.Evaluate(
            InstallerOperationMode.Install,
            detectStatus: 0,
            currentBundleVersion: "1.0.0.0",
            highestDetectedInstalledVersion: null,
            registryInstalledVersion: null,
            installedExecutableVersion: "1.0.0.0",
            compareVersions: CompareVersions);

        Assert.True(decision.ShouldSkipInstall);
        Assert.False(decision.IsNewerVersion);
        Assert.Equal("1.0.0.0", decision.InstalledVersion);
    }

    [Fact]
    public void InstallerUpToDateDecision_Install_WhenOlderRelatedBundleNeedsCleanup_StillRequiresConsent()
    {
        var decision = InstallerUpToDateDecision.Evaluate(
            InstallerOperationMode.Install,
            detectStatus: 0,
            currentBundleVersion: "1.0.1",
            highestDetectedInstalledVersion: "1.0.1",
            registryInstalledVersion: null,
            installedExecutableVersion: "1.0.1",
            compareVersions: CompareVersions,
            hasOlderRelatedBundle: true);

        Assert.True(decision.ShouldSkipInstall);
        Assert.True(decision.RequiresReinstallConfirmation);
        Assert.Equal("1.0.1", decision.InstalledVersion);
    }

    private static int CompareVersions(string left, string right) =>
        Version.Parse(left).CompareTo(Version.Parse(right));
}
