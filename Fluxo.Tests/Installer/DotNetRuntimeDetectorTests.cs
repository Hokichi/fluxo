using Fluxo.Installer.Models;
using Fluxo.Installer.Services;
using Fluxo.Installer.ViewModels;
using Fluxo.Tests.TestSupport;
using Xunit;

namespace Fluxo.Tests.Installer;

public sealed class DotNetRuntimeDetectorTests
{
    [Fact]
    public void ReturnsFalse_When_RuntimeMissing()
    {
        var detector = new DotNetRuntimeDetector(
            requiredMajorVersion: 10,
            runtimeListProvider: static () => WindowsPathFixtures.RuntimeListEntry(9));

        var result = detector.IsRequiredRuntimeInstalled();

        Assert.False(result);
    }

    [Fact]
    public void ReturnsTrue_When_WindowsDesktopRuntimePresent()
    {
        var detector = new DotNetRuntimeDetector(
            requiredMajorVersion: 10,
            runtimeListProvider: static () => WindowsPathFixtures.WindowsDesktopRuntimeListEntry(10));

        var result = detector.IsRequiredRuntimeInstalled();

        Assert.True(result);
    }

    [Fact]
    public void ReturnsFalse_When_OnlyBaseRuntimePresent()
    {
        var detector = new DotNetRuntimeDetector(
            requiredMajorVersion: 10,
            runtimeListProvider: static () => WindowsPathFixtures.RuntimeListEntry(10));

        var result = detector.IsRequiredRuntimeInstalled();

        Assert.False(result);
    }

    [Fact]
    public async Task InstallCommand_Continues_When_RuntimeInstallerInstallsRuntime()
    {
        var detectCalls = 0;
        var vm = new InstallerViewModel(
            dotNetRuntimeDetector: new FixedRuntimeDetector(false),
            runtimeInstaller: new DelegateRuntimeInstaller(
                ensureInstalledAsync: _ => Task.FromResult(new DotNetRuntimeInstallResult(
                    DotNetRuntimeInstallStatus.InstalledByFluxo,
                    "installed"))),
            requestDetect: () => detectCalls++,
            getRunningFluxoProcessIds: static () => []);

        await vm.InstallCommand.ExecuteAsync(null);

        Assert.Equal(1, detectCalls);
        Assert.Equal(InstallerScreen.Progress, vm.Screen);
        Assert.Equal(InstallerState.Installing, vm.State);
        Assert.Equal("Detecting installation state...", vm.StatusMessage);
    }

    [Fact]
    public void ReturnsFalse_When_RuntimeListProviderReturnsNull()
    {
        var detector = new DotNetRuntimeDetector(
            requiredMajorVersion: 10,
            runtimeListProvider: static () => null);

        var result = detector.IsRequiredRuntimeInstalled();

        Assert.False(result);
    }

    [Fact]
    public void ReturnsFalse_When_RuntimeListProviderThrows()
    {
        var detector = new DotNetRuntimeDetector(
            requiredMajorVersion: 10,
            runtimeListProvider: static () => throw new InvalidOperationException("provider failure"));

        var result = detector.IsRequiredRuntimeInstalled();

        Assert.False(result);
    }

    private sealed class FixedRuntimeDetector(bool isInstalled) : IDotNetRuntimeDetector
    {
        public bool IsRequiredRuntimeInstalled() => isInstalled;
    }

    private sealed class DelegateRuntimeInstaller(
        Func<CancellationToken, Task<DotNetRuntimeInstallResult>>? ensureInstalledAsync = null)
        : IDotNetRuntimeInstaller
    {
        public Task<DotNetRuntimeInstallResult> EnsureInstalledAsync(CancellationToken cancellationToken) =>
            ensureInstalledAsync?.Invoke(cancellationToken)
            ?? Task.FromResult(new DotNetRuntimeInstallResult(
                DotNetRuntimeInstallStatus.AlreadyInstalled,
                "already installed"));

        public Task RollbackRuntimeInstalledByFluxoAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<DotNetRuntimeUninstallResult> UninstallOwnedRuntimeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new DotNetRuntimeUninstallResult(
                DotNetRuntimeUninstallStatus.Skipped,
                "skipped"));

        public void RequestCancellation()
        {
        }

        public void CleanupDownloadedInstaller()
        {
        }
    }
}
