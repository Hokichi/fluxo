using Fluxo.Installer.Models;
using Fluxo.Installer.Services;
using Fluxo.Installer.ViewModels;
using NSubstitute;
using WixToolset.BootstrapperApplicationApi;
using Xunit;

namespace Fluxo.Tests.Installer;

public sealed class InstallerStartupTests
{
    private const string Folder = @"D:\My Apps\fluxo";
    private readonly List<string> events = [];
    private readonly IDotNetRuntimeInstaller runtime = Substitute.For<IDotNetRuntimeInstaller>();
    private readonly ILegacySelfContainedCleanupService cleanup = Substitute.For<ILegacySelfContainedCleanupService>();

    private InstallerViewModel Create(bool running = false, bool allowStop = true, bool allowCancel = false)
    {
        runtime.EnsureInstalledAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            events.Add("runtime");
            return Task.FromResult(new DotNetRuntimeInstallResult(DotNetRuntimeInstallStatus.AlreadyInstalled, "present"));
        });
        cleanup.Cleanup(Arg.Any<string>()).Returns(new LegacyCleanupResult(true, "done"));
        return new InstallerViewModel(runtimeInstaller: runtime, legacyCleanupService: cleanup,
            requestDetect: () => events.Add("detect"), requestPlan: () => events.Add("plan"),
            requestApply: () => events.Add("apply"), setInstallFolderVariable: path => events.Add(path),
            getRunningFluxoProcessIds: () => running ? [123] : [],
            requestTerminateRunningAppConfirmation: _ => { events.Add("stop?"); return allowStop; },
            tryTerminateProcessById: _ => { events.Add("stop"); return true; },
            fileExists: _ => true, directoryExists: _ => true, copyFile: (_, _, _) => { },
            bundleExecutablePath: @"C:\Downloads\fluxo.exe", requestRollback: () => true,
            requestCancelConfirmation: () => allowCancel, deleteDirectory: _ => throw new Exception("Unexpected delete"),
            launchInstalledApp: path => events.Add(path), closeInstallerAction: () => events.Add("close"));
    }

    private static InstallerUpToDateDecisionResult Same => new(true, "1.0.6", false, true);

    [Fact]
    public void Startup_IsReadOnlyAndIdempotent()
    {
        var vm = Create(running: true);
        vm.Begin();
        vm.Begin();
        Assert.Equal(["detect"], events);
        Assert.Equal(InstallerState.Detecting, vm.State);
        Assert.False(vm.InstallCommand.CanExecute(null));
    }

    [Fact]
    public void Decline_ReachesFinishedAndLaunchesExistingFolder_WithoutStoppingApp()
    {
        var vm = Create(running: true);
        vm.Begin();
        vm.OnStartupDetectionComplete(0, Same, Folder);
        Assert.Equal(InstallerScreen.ReinstallConfirmation, vm.Screen);
        vm.DeclineReinstallCommand.Execute(null);
        Assert.Equal(InstallerState.FinishedUpToDate, vm.State);
        Assert.Equal(InstallerScreen.Finished, vm.Screen);
        Assert.Equal(0, vm.ExitCode);
        Assert.Equal(["detect"], events);
        vm.LaunchAppCommand.Execute(null);
        Assert.Equal(["detect", Folder + @"\fluxo.exe", "close"], events);
        cleanup.DidNotReceive().Cleanup(Arg.Any<string>());
    }

    [Fact]
    public async Task Accept_RepairsAtDetectedFolder_AndIgnoresRepeatedDetection()
    {
        var vm = Create();
        vm.Begin();
        vm.OnStartupDetectionComplete(0, Same, Folder);
        await vm.ReinstallCommand.ExecuteAsync(null);
        await vm.ReinstallCommand.ExecuteAsync(null);
        vm.OnStartupDetectionComplete(0, Same, Folder);
        Assert.Equal(InstallerRequestedOperation.Repair, vm.RequestedOperation);
        Assert.Equal(LaunchAction.Repair, InstallerLaunchActionResolver.Resolve(vm.RequestedOperation));
        vm.OnDetectComplete(0);
        vm.OnDetectComplete(0);
        Assert.Single(events, e => e == "plan");
        vm.OnPlanComplete(0);
        vm.OnApplyComplete(0);
        Assert.Equal(InstallerState.FinishedSuccess, vm.State);
        Assert.Contains(Folder, events);
        cleanup.Received(1).Cleanup(Folder);
    }

    [Theory]
    [InlineData(false, "1.0.5", InstallerState.Welcome)]
    [InlineData(false, null, InstallerState.Welcome)]
    [InlineData(true, "1.0.7", InstallerState.FinishedUpToDate)]
    public void Startup_RoutesFreshOlderAndNewer(bool newer, string? version, InstallerState expected)
    {
        var vm = Create();
        vm.Begin();
        vm.OnStartupDetectionComplete(0, new(newer, version, newer), Folder);
        Assert.Equal(expected, vm.State);
        Assert.Equal(Folder, vm.InstallFolder);
        Assert.Equal(["detect"], events);
    }

    [Fact]
    public void FailedStartupDetection_DoesNotPlan()
    {
        var vm = Create();
        vm.Begin();
        vm.OnStartupDetectionComplete(5, Same, Folder);
        Assert.Equal(InstallerState.FinishedFailed, vm.State);
        Assert.Contains("detect", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["detect"], events);
    }

    [Fact]
    public async Task RefusedTermination_DoesNotInstallRuntimeOrPlan()
    {
        var vm = Create(true, false);
        vm.Begin();
        vm.OnStartupDetectionComplete(0, Same, Folder);
        await vm.ReinstallCommand.ExecuteAsync(null);
        Assert.Equal(InstallerState.FinishedFailed, vm.State);
        Assert.Equal(["detect", "stop?"], events);
    }

    [Fact]
    public async Task RuntimeFailure_DoesNotDetectOrPlanRepair()
    {
        var vm = Create();
        runtime.EnsureInstalledAsync(Arg.Any<CancellationToken>()).Returns(
            Task.FromResult(new DotNetRuntimeInstallResult(DotNetRuntimeInstallStatus.Failed, "offline")));
        vm.Begin();
        vm.OnStartupDetectionComplete(0, Same, Folder);
        await vm.ReinstallCommand.ExecuteAsync(null);
        Assert.Equal(InstallerState.FinishedFailed, vm.State);
        Assert.DoesNotContain("plan", events);
        Assert.Single(events, e => e == "detect");
    }

    [Fact]
    public async Task CancelWhileCheckingRuntime_DoesNotStartRepairWhenRuntimeCompletes()
    {
        var vm = Create(allowCancel: true);
        var completion = new TaskCompletionSource<DotNetRuntimeInstallResult>();
        CancellationToken runtimeToken = default;
        runtime.EnsureInstalledAsync(Arg.Any<CancellationToken>()).Returns(call =>
        {
            runtimeToken = call.Arg<CancellationToken>();
            return completion.Task;
        });
        vm.Begin();
        vm.OnStartupDetectionComplete(0, Same, Folder);
        var operation = vm.ReinstallCommand.ExecuteAsync(null);
        vm.CloseInstallerCommand.Execute(null);
        completion.SetResult(new(DotNetRuntimeInstallStatus.AlreadyInstalled, "present"));
        await operation;
        Assert.True(runtimeToken.IsCancellationRequested);
        Assert.Equal(InstallerState.FinishedCancelled, vm.State);
        Assert.Single(events, e => e == "detect");
    }

    [Fact]
    public void LateOperationDetection_AfterDecline_DoesNotPlan()
    {
        var vm = Create();
        vm.Begin();
        vm.OnStartupDetectionComplete(0, Same, Folder);
        vm.DeclineReinstallCommand.Execute(null);
        vm.OnDetectComplete(0);
        Assert.DoesNotContain("plan", events);
    }

    [Fact]
    public void FailedStartup_DisablesInstallAndFolderChanges()
    {
        var vm = Create();
        vm.Begin();
        vm.OnStartupDetectionComplete(1, Same, Folder);
        Assert.False(vm.InstallCommand.CanExecute(null));
        Assert.False(vm.ChangeDirectoryCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(InstallerRequestedOperation.Install, LaunchAction.Install)]
    [InlineData(InstallerRequestedOperation.Repair, LaunchAction.Repair)]
    [InlineData(InstallerRequestedOperation.Uninstall, LaunchAction.Uninstall)]
    public void LaunchAction_MatchesRequestedOperation(InstallerRequestedOperation operation, LaunchAction expected)
        => Assert.Equal(expected, InstallerLaunchActionResolver.Resolve(operation));

    [Fact]
    public async Task NewerVersionDiscoveredAfterConsent_DoesNotRepairOrDowngrade()
    {
        var vm = Create();
        vm.Begin();
        vm.OnStartupDetectionComplete(0, Same, Folder);
        await vm.ReinstallCommand.ExecuteAsync(null);
        vm.OnInstallationDetected(0, new(true, "1.0.7", true), Folder);
        Assert.Equal(InstallerState.FinishedUpToDate, vm.State);
        Assert.Contains("1.0.7", vm.FinishedSubtitle);
        Assert.DoesNotContain("plan", events);
    }

    [Fact]
    public async Task EqualVersionDuringOperationDetection_DoesNotRepeatAcceptedPrompt()
    {
        var vm = Create();
        vm.Begin();
        vm.OnInstallationDetected(0, Same, Folder);
        await vm.ReinstallCommand.ExecuteAsync(null);
        vm.OnInstallationDetected(0, Same, Folder);
        Assert.Equal(InstallerScreen.Progress, vm.Screen);
        Assert.Single(events, e => e == "plan");
    }
}
