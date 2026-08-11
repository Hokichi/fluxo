using Fluxo.Installer.Models;
using Fluxo.Installer.Services;
using Fluxo.Installer.ViewModels;
using Fluxo.Tests.TestSupport;
using Xunit;

namespace Fluxo.Tests.Installer;

public sealed class InstallerLaunchCommandTests
{
    [Fact]
    public void InstallerLaunchCommand_LaunchApp_UsesInstalledExePath()
    {
        string? launchedPath = null;
        var vm = new InstallerViewModel(
            dotNetRuntimeDetector: new FixedRuntimeDetector(true),
            fileExists: static _ => true,
            bundleExecutablePath: WindowsPathFixtures.DefaultInstaller,
            copyFile: static (_, _, _) => { },
            launchInstalledApp: path => launchedPath = path);

        vm.OnApplyComplete(0);

        Assert.Equal(InstallerState.FinishedSuccess, vm.State);
        Assert.True(vm.LaunchAppCommand.CanExecute(null));

        vm.LaunchAppCommand.Execute(null);

        Assert.Equal(WindowsPathFixtures.InstalledExecutable, launchedPath);
    }

    [Fact]
    public void InstallerLaunchCommand_LaunchApp_Disabled_WhenInstallFailed()
    {
        var launchCalls = 0;
        var vm = new InstallerViewModel(
            dotNetRuntimeDetector: new FixedRuntimeDetector(true),
            fileExists: static _ => false,
            bundleExecutablePath: WindowsPathFixtures.DefaultInstaller,
            copyFile: static (_, _, _) => { },
            launchInstalledApp: _ => launchCalls++);

        vm.OnApplyComplete(0);

        Assert.Equal(InstallerState.FinishedFailed, vm.State);
        Assert.False(vm.LaunchAppCommand.CanExecute(null));

        vm.LaunchAppCommand.Execute(null);

        Assert.Equal(0, launchCalls);
    }

    [Fact]
    public void InstallerLaunchCommand_LaunchApp_Enabled_WhenVersionIsUpToDate()
    {
        string? launchedPath = null;
        var vm = new InstallerViewModel(
            dotNetRuntimeDetector: new FixedRuntimeDetector(true),
            fileExists: static _ => true,
            bundleExecutablePath: WindowsPathFixtures.DefaultInstaller,
            copyFile: static (_, _, _) => { },
            launchInstalledApp: path => launchedPath = path);

        vm.OnDetectedUpToDateVersion();

        Assert.Equal(InstallerState.FinishedUpToDate, vm.State);
        Assert.True(vm.LaunchAppCommand.CanExecute(null));

        vm.LaunchAppCommand.Execute(null);

        Assert.Equal(WindowsPathFixtures.InstalledExecutable, launchedPath);
    }

    [Fact]
    public void InstallerLaunchCommand_LaunchApp_UsesResolvedInstallFolder_WhenVersionIsUpToDate()
    {
        string? launchedPath = null;
        var vm = new InstallerViewModel(
            dotNetRuntimeDetector: new FixedRuntimeDetector(true),
            fileExists: static _ => true,
            bundleExecutablePath: WindowsPathFixtures.DefaultInstaller,
            copyFile: static (_, _, _) => { },
            launchInstalledApp: path => launchedPath = path);

        vm.OnDetectedUpToDateVersion(installFolder: WindowsPathFixtures.AppsFluxoFolder);

        Assert.Equal(InstallerState.FinishedUpToDate, vm.State);
        Assert.True(vm.LaunchAppCommand.CanExecute(null));

        vm.LaunchAppCommand.Execute(null);

        Assert.Equal(Path.Combine(WindowsPathFixtures.AppsFluxoFolder, "fluxo.exe"), launchedPath);
    }

    [Fact]
    public void InstallerLaunchCommand_Constructor_UsesRequestedInstallFolder_WhenPathIsValid()
    {
        var vm = new InstallerViewModel(
            requestedInstallFolder: WindowsPathFixtures.AppsFluxoFolder,
            bundleExecutablePath: WindowsPathFixtures.DefaultInstaller);

        Assert.Equal(WindowsPathFixtures.AppsFluxoFolder, vm.InstallFolder);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative\\fluxo")]
    public void InstallerLaunchCommand_Constructor_IgnoresRequestedInstallFolderWhenPathIsInvalid(string requestedInstallFolder)
    {
        var vm = new InstallerViewModel(
            requestedInstallFolder: requestedInstallFolder,
            bundleExecutablePath: WindowsPathFixtures.DefaultInstaller);

        Assert.Equal(WindowsPathFixtures.ProgramFilesFluxoFolder, vm.InstallFolder);
    }

    [Fact]
    public void InstallerLaunchCommand_LaunchApp_DoesNotCloseInstaller_WhenExecutableCannotBeFound()
    {
        var closeCalls = 0;
        var launchCalls = 0;
        var vm = new InstallerViewModel(
            dotNetRuntimeDetector: new FixedRuntimeDetector(true),
            fileExists: static _ => false,
            launchInstalledApp: _ => launchCalls++,
            closeInstallerAction: () => closeCalls++);

        vm.OnDetectedUpToDateVersion();

        Assert.True(vm.LaunchAppCommand.CanExecute(null));

        vm.LaunchAppCommand.Execute(null);

        Assert.Equal(0, launchCalls);
        Assert.Equal(0, closeCalls);
        Assert.Equal("Fluxo executable was not found. Setup will stay open.", vm.StatusMessage);
    }

    [Fact]
    public void InstallerLaunchCommand_LaunchApp_DoesNotCloseInstaller_WhenLaunchFails()
    {
        var closeCalls = 0;
        var vm = new InstallerViewModel(
            dotNetRuntimeDetector: new FixedRuntimeDetector(true),
            fileExists: static _ => true,
            launchInstalledApp: _ => throw new InvalidOperationException("start failed"),
            closeInstallerAction: () => closeCalls++);

        vm.OnDetectedUpToDateVersion();

        Assert.True(vm.LaunchAppCommand.CanExecute(null));

        vm.LaunchAppCommand.Execute(null);

        Assert.Equal(0, closeCalls);
        Assert.Equal("Launch reported an error: start failed", vm.StatusMessage);
    }

    [Fact]
    public void InstallerLaunchCommand_LaunchApp_Disabled_WhenUninstallCompleted()
    {
        var launchCalls = 0;
        var vm = new InstallerViewModel(
            dotNetRuntimeDetector: new FixedRuntimeDetector(true),
            operationMode: InstallerOperationMode.Uninstall,
            fileExists: static _ => true,
            directoryExists: static _ => false,
            deleteLocalMachineRegistrySubKeyTree: static (_, _) => { },
            getRunningFluxoProcessIds: static () => [],
            bundleExecutablePath: WindowsPathFixtures.DefaultInstaller,
            copyFile: static (_, _, _) => { },
            launchInstalledApp: _ => launchCalls++);

        vm.Begin();
        vm.OnApplyComplete(0);

        Assert.Equal(InstallerState.FinishedUninstalled, vm.State);
        Assert.False(vm.LaunchAppCommand.CanExecute(null));

        vm.LaunchAppCommand.Execute(null);
        Assert.Equal(0, launchCalls);
    }

    private sealed class FixedRuntimeDetector(bool isInstalled) : IDotNetRuntimeDetector
    {
        public bool IsRequiredRuntimeInstalled() => isInstalled;
    }
}
