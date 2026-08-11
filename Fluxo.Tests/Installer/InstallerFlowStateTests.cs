using Fluxo.Installer.Models;
using Fluxo.Installer.Services;
using Fluxo.Installer.ViewModels;
using Fluxo.Tests.TestSupport;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace Fluxo.Tests.Installer;

public sealed class InstallerFlowStateTests
{
    [Fact]
    public async Task InstallerFlowState_InstallCommand_TriggersDetect_AndMovesToInstalling()
    {
        var detectCalls = 0;
        var vm = CreateViewModel(
            requestDetect: () => detectCalls++,
            fileExists: static _ => true);

        await vm.InstallCommand.ExecuteAsync(null);

        Assert.Equal(InstallerState.Installing, vm.State);
        Assert.Equal("Detecting installation state...", vm.StatusMessage);
        Assert.Equal(1, detectCalls);
    }

    [Fact]
    public async Task InstallerFlowState_InstallCommand_SetsInstallFolderBeforeDetect()
    {
        var folderVariableValues = new List<string>();
        var vm = CreateViewModel(
            setInstallFolderVariable: value => folderVariableValues.Add(value),
            requestDetect: static () => { },
            fileExists: static _ => true);
        vm.InstallFolder = WindowsPathFixtures.AppsFluxoFolder;

        await vm.InstallCommand.ExecuteAsync(null);

        Assert.Single(folderVariableValues);
        Assert.Equal(WindowsPathFixtures.AppsFluxoFolder, folderVariableValues[0]);
    }

    [Fact]
    public async Task InstallerFlowState_InstallCommand_InstallsRuntimeInsideInstallingStep_BeforeDetect()
    {
        var events = new List<string>();
        var vm = CreateViewModel(
            runtimeInstaller: new DelegateRuntimeInstaller(
                ensureInstalledAsync: _ =>
                {
                    events.Add("runtime");
                    return Task.FromResult(new DotNetRuntimeInstallResult(
                        DotNetRuntimeInstallStatus.InstalledByFluxo,
                        "installed"));
                }),
            requestDetect: () => events.Add("detect"),
            fileExists: static _ => true);

        await vm.InstallCommand.ExecuteAsync(null);

        Assert.Equal(["runtime", "detect"], events);
        Assert.Equal(InstallerState.Installing, vm.State);
        Assert.Equal("Detecting installation state...", vm.StatusMessage);
    }

    [Fact]
    public async Task InstallerFlowState_InstallCommand_RollsBackAppInstallation_WhenRuntimeInstallFails()
    {
        var rollbackCalls = 0;
        var vm = CreateViewModel(
            runtimeInstaller: new DelegateRuntimeInstaller(
                ensureInstalledAsync: _ => Task.FromResult(new DotNetRuntimeInstallResult(
                    DotNetRuntimeInstallStatus.Failed,
                    "download failed"))),
            requestRollback: () =>
            {
                rollbackCalls++;
                return true;
            },
            requestDetect: static () => throw new InvalidOperationException("Detect should not run."),
            fileExists: static _ => true);

        await vm.InstallCommand.ExecuteAsync(null);

        Assert.Equal(1, rollbackCalls);
        Assert.Equal(InstallerState.FinishedFailed, vm.State);
        Assert.Equal(InstallerScreen.Finished, vm.Screen);
        Assert.Equal("Runtime installation failed: download failed Rollback completed.", vm.StatusMessage);
    }

    [Fact]
    public void InstallerFlowState_PlanComplete_Success_TransitionsToApplying()
    {
        var applyCalls = 0;
        var folderVariableValues = new List<string>();
        var vm = CreateViewModel(
            setInstallFolderVariable: value => folderVariableValues.Add(value),
            requestApply: () => applyCalls++,
            fileExists: static _ => true);

        vm.OnPlanComplete(0);

        Assert.Equal(InstallerState.Installing, vm.State);
        Assert.Equal("Installing files...", vm.StatusMessage);
        Assert.Single(folderVariableValues);
        Assert.Equal(vm.InstallFolder, folderVariableValues[0]);
        Assert.Equal(1, applyCalls);
    }

    [Fact]
    public async Task InstallerFlowState_DetectComplete_SuccessRequestsPlan_AndKeepsInstallingState()
    {
        var detectCalls = 0;
        var planCalls = 0;
        var folderVariableValues = new List<string>();
        var vm = CreateViewModel(
            setInstallFolderVariable: value => folderVariableValues.Add(value),
            requestDetect: () => detectCalls++,
            requestPlan: () => planCalls++,
            fileExists: static _ => true);

        await vm.InstallCommand.ExecuteAsync(null);
        vm.OnDetectComplete(0);

        Assert.Equal(InstallerState.Installing, vm.State);
        Assert.Equal("Planning installation...", vm.StatusMessage);
        Assert.Single(folderVariableValues);
        Assert.Equal(vm.InstallFolder, folderVariableValues[0]);
        Assert.Equal(1, detectCalls);
        Assert.Equal(1, planCalls);
    }

    [Fact]
    public void InstallerFlowState_DetectComplete_Failure_ContinuesToPlanning()
    {
        var planCalls = 0;
        var folderVariableValues = new List<string>();
        var vm = CreateViewModel(
            setInstallFolderVariable: value => folderVariableValues.Add(value),
            requestPlan: () => planCalls++,
            fileExists: static _ => true);

        vm.OnDetectComplete(1);

        Assert.Equal(InstallerState.Welcome, vm.State);
        Assert.Equal("Planning installation...", vm.StatusMessage);
        Assert.Single(folderVariableValues);
        Assert.Equal(vm.InstallFolder, folderVariableValues[0]);
        Assert.Equal(1, planCalls);
    }

    [Fact]
    public void InstallerFlowState_CloseInstaller_OnWelcomeDeclineCancellation_KeepsInstallerOpen()
    {
        var closeCalls = 0;
        var vm = CreateViewModel(
            requestCancelConfirmation: () => false,
            closeInstallerAction: () => closeCalls++);

        vm.CloseInstallerCommand.Execute(null);

        Assert.Equal(0, closeCalls);
        Assert.Equal(InstallerState.Welcome, vm.State);
        Assert.Equal(InstallerScreen.Welcome, vm.Screen);
    }

    [Fact]
    public void InstallerFlowState_CloseInstaller_OnWelcomeConfirmCancellation_TransitionsToFinishedCancelled()
    {
        var closeCalls = 0;
        var vm = CreateViewModel(
            requestCancelConfirmation: () => true,
            closeInstallerAction: () => closeCalls++);

        vm.CloseInstallerCommand.Execute(null);

        Assert.Equal(0, closeCalls);
        Assert.Equal(InstallerState.FinishedCancelled, vm.State);
        Assert.Equal(InstallerScreen.Finished, vm.Screen);
        Assert.Equal("Installation cancelled.", vm.StatusMessage);
        Assert.Equal("Installation cancelled", vm.FinishedTitle);
        Assert.Equal("Please close the setup and run it again", vm.FinishedSubtitle);
    }

    [Fact]
    public async Task InstallerFlowState_CloseInstaller_DuringInstallConfirmCancellationRunsRollback_AndShowsRollbackChecklistOnly()
    {
        var rollbackCalls = 0;
        var runtimeCancellationCalls = 0;
        var runtimeRollbackCalls = 0;
        var vm = CreateViewModel(
            requestRollback: () =>
            {
                rollbackCalls++;
                return true;
            },
            runtimeInstaller: new DelegateRuntimeInstaller(
                requestCancellation: () => runtimeCancellationCalls++,
                rollbackRuntimeInstalledByFluxoAsync: _ =>
                {
                    runtimeRollbackCalls++;
                    return Task.CompletedTask;
                }),
            requestCancelConfirmation: () => true,
            requestDetect: static () => { },
            fileExists: static _ => true);

        await vm.InstallCommand.ExecuteAsync(null);
        vm.CloseInstallerCommand.Execute(null);

        Assert.Equal(1, rollbackCalls);
        Assert.Equal(1, runtimeCancellationCalls);
        Assert.Equal(1, runtimeRollbackCalls);
        Assert.Equal(InstallerState.FinishedCancelled, vm.State);
        Assert.Equal(InstallerScreen.Finished, vm.Screen);
        Assert.Single(vm.ChecklistSteps);
        Assert.Equal("Rolling back", vm.ChecklistSteps[0].Label);
        Assert.Equal(ChecklistStepState.Success, vm.ChecklistSteps[0].State);
        Assert.Equal("Installation cancelled", vm.FinishedTitle);
    }

    [Fact]
    public async Task InstallerFlowState_DetectComplete_FailureAfterInstallStart_ContinuesToPlanning()
    {
        var planCalls = 0;
        var vm = CreateViewModel(
            requestPlan: () => planCalls++,
            requestDetect: static () => { },
            fileExists: static _ => true);

        await vm.InstallCommand.ExecuteAsync(null);
        vm.OnDetectComplete(1);

        Assert.Equal(1, planCalls);
        Assert.Equal(InstallerState.Installing, vm.State);
        Assert.Equal(InstallerScreen.Progress, vm.Screen);
        Assert.Equal("Planning installation...", vm.StatusMessage);
    }

    [Fact]
    public void InstallerFlowState_DetectedUpToDateVersion_TransitionsToFinishedPage_WithExpectedSubtitle()
    {
        var vm = CreateViewModel(fileExists: static _ => true);

        vm.OnDetectedUpToDateVersion();

        Assert.Equal(InstallerState.FinishedUpToDate, vm.State);
        Assert.Equal(InstallerScreen.Finished, vm.Screen);
        Assert.Equal("Let's begin", vm.FinishedTitle);
        Assert.Equal("Version is up-to-date.", vm.FinishedSubtitle);
        Assert.Equal(0, vm.ExitCode);
    }

    [Fact]
    public void InstallerFlowState_DetectedNewerVersion_TransitionsToFinishedPage_WithNewerVersionSubtitle()
    {
        var vm = CreateViewModel(fileExists: static _ => true);

        vm.OnDetectedUpToDateVersion("1.2.3.4", isNewerVersion: true);

        Assert.Equal(InstallerState.FinishedUpToDate, vm.State);
        Assert.Equal(InstallerScreen.Finished, vm.Screen);
        Assert.Equal("Newer version found: 1.2.3.4", vm.FinishedSubtitle);
    }

    [Fact]
    public void InstallerFlowState_ApplyComplete_SuccessTransitionsToVerifying_ThenSuccess()
    {
        var observedStates = new List<InstallerState>();
        var cleanupCalls = 0;
        InstallerViewModel? vm = null;
        vm = CreateViewModel(
            legacyCleanupService: new DelegateLegacyCleanupService(_ =>
            {
                cleanupCalls++;
                Assert.NotNull(vm);
                Assert.Equal(InstallerState.Verifying, vm!.State);
                Assert.Equal("Cleaning up...", vm.StatusMessage);
                return new LegacyCleanupResult(true, "cleaned");
            }),
            fileExists: static _ => true);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(InstallerViewModel.State))
            {
                observedStates.Add(vm.State);
            }
        };

        vm.OnApplyComplete(0);

        Assert.Contains(InstallerState.Verifying, observedStates);
        Assert.Equal(1, cleanupCalls);
        Assert.Equal(InstallerState.FinishedSuccess, vm.State);
        Assert.Equal("Installation complete.", vm.StatusMessage);
        Assert.Equal(0, vm.ExitCode);
    }

    [Fact]
    public async Task InstallerFlowState_ApplyComplete_CleanupFailure_TransitionsToFailed()
    {
        var vm = CreateViewModel(
            requestRollback: static () => true,
            requestDetect: static () => { },
            legacyCleanupService: new DelegateLegacyCleanupService(
                _ => new LegacyCleanupResult(false, "access denied")),
            fileExists: static _ => true);

        await vm.InstallCommand.ExecuteAsync(null);
        vm.OnApplyComplete(0);

        Assert.Equal(InstallerState.FinishedFailed, vm.State);
        Assert.Equal("Cleanup failed: access denied Rollback completed.", vm.StatusMessage);
    }

    [Fact]
    public void InstallerFlowState_Begin_UninstallModeSkipsWelcome_AndStartsDetection()
    {
        var detectCalls = 0;
        var vm = CreateViewModel(
            requestDetect: () => detectCalls++,
            operationMode: InstallerOperationMode.Uninstall);

        vm.Begin();

        Assert.Equal(InstallerScreen.Uninstall, vm.Screen);
        Assert.Equal(InstallerState.Installing, vm.State);
        Assert.Equal("Detecting installed version...", vm.StatusMessage);
        Assert.Equal(1, detectCalls);
    }

    [Fact]
    public void InstallerFlowState_Begin_RepairerBundlePathSkipsWelcome_EvenWhenOperationModeDefaultsToInstall()
    {
        var vm = new InstallerViewModel(
            dotNetRuntimeDetector: new FixedRuntimeDetector(true),
            bundleExecutablePath: WindowsPathFixtures.AlternateRepairerExecutable,
            copyFile: static (_, _, _) => { },
            getRunningFluxoProcessIds: static () => []);

        vm.Begin();

        Assert.True(vm.IsMaintenanceMode);
        Assert.False(vm.IsUninstallMode);
        Assert.Equal(InstallerScreen.AppFound, vm.Screen);
        Assert.Equal(InstallerState.Welcome, vm.State);
    }

    [Fact]
    public void InstallerFlowState_Begin_MaintenanceMode_ShowsAppFoundPage()
    {
        var vm = CreateViewModel(operationMode: InstallerOperationMode.Maintenance);

        vm.Begin();

        Assert.Equal(InstallerScreen.AppFound, vm.Screen);
        Assert.Equal(InstallerState.Welcome, vm.State);
        Assert.Equal(InstallerMaintenanceAction.Repair, vm.SelectedMaintenanceAction);
        Assert.Equal(InstallerRequestedOperation.Install, vm.RequestedOperation);
    }

    [Fact]
    public void InstallerFlowState_Begin_RepairerBundlePath_ShowsAppFoundPage()
    {
        var vm = new InstallerViewModel(
            dotNetRuntimeDetector: new FixedRuntimeDetector(true),
            bundleExecutablePath: WindowsPathFixtures.AlternateRepairerExecutable,
            copyFile: static (_, _, _) => { },
            getRunningFluxoProcessIds: static () => []);

        vm.Begin();

        Assert.Equal(InstallerScreen.AppFound, vm.Screen);
        Assert.False(vm.IsUninstallMode);
        Assert.True(vm.IsMaintenanceMode);
    }

    [Fact]
    public void InstallerFlowState_Begin_InstallModeWhenFluxoRunningAndUserDeclinesTermination_BlocksBeforeAnyAction()
    {
        var vm = CreateViewModel(
            getRunningFluxoProcessIds: static () => [1234],
            requestTerminateRunningAppConfirmation: _ => false,
            fileExists: static _ => true);

        vm.Begin();

        Assert.Equal(InstallerState.FinishedFailed, vm.State);
        Assert.Equal(InstallerScreen.Finished, vm.Screen);
        Assert.Equal(
            "Installation did not run because fluxo is still open. Please close fluxo and run setup again.",
            vm.StatusMessage);
    }

    [Fact]
    public void InstallerFlowState_Begin_MaintenanceModeWhenFluxoRunningAndUserDeclinesTermination_BlocksBeforeActionSelection()
    {
        var vm = CreateViewModel(
            operationMode: InstallerOperationMode.Maintenance,
            getRunningFluxoProcessIds: static () => [1234],
            requestTerminateRunningAppConfirmation: _ => false,
            fileExists: static _ => true);

        vm.Begin();

        Assert.Equal(InstallerState.FinishedFailed, vm.State);
        Assert.Equal(InstallerScreen.Finished, vm.Screen);
        Assert.Equal(
            "Repair did not run because fluxo is still open. Please close fluxo and run the repairer again.",
            vm.StatusMessage);
    }

    [Fact]
    public void InstallerFlowState_Begin_UninstallModeWhenFluxoRunningAndUserDeclinesTermination_BlocksBeforeDetect()
    {
        var detectCalls = 0;
        var vm = CreateViewModel(
            requestDetect: () => detectCalls++,
            operationMode: InstallerOperationMode.Uninstall,
            getRunningFluxoProcessIds: static () => [1234],
            requestTerminateRunningAppConfirmation: _ => false);

        vm.Begin();

        Assert.Equal(InstallerState.FinishedFailed, vm.State);
        Assert.Equal(InstallerScreen.Finished, vm.Screen);
        Assert.Equal(0, detectCalls);
        Assert.Equal(
            "Uninstallation did not run because fluxo is still open. Please close fluxo and run the repairer again.",
            vm.StatusMessage);
    }

    [Fact]
    public void InstallerFlowState_ContinueMaintenance_DefaultRepair_RequestsDetectAndSetsRepairOperation()
    {
        var detectCalls = 0;
        var vm = CreateViewModel(
            requestDetect: () => detectCalls++,
            operationMode: InstallerOperationMode.Maintenance);

        vm.Begin();
        vm.ContinueMaintenanceCommand.Execute(null);

        Assert.Equal(InstallerRequestedOperation.Repair, vm.RequestedOperation);
        Assert.Equal(InstallerScreen.Progress, vm.Screen);
        Assert.Equal("Detecting installation state...", vm.StatusMessage);
        Assert.Equal(1, detectCalls);
    }

    [Fact]
    public void InstallerFlowState_ContinueMaintenance_Uninstall_RequestsDetectAndSetsUninstallOperation()
    {
        var detectCalls = 0;
        var vm = CreateViewModel(
            requestDetect: () => detectCalls++,
            operationMode: InstallerOperationMode.Maintenance);

        vm.Begin();
        vm.SelectedMaintenanceAction = InstallerMaintenanceAction.Uninstall;
        vm.ContinueMaintenanceCommand.Execute(null);

        Assert.Equal(InstallerRequestedOperation.Uninstall, vm.RequestedOperation);
        Assert.Equal(InstallerScreen.Uninstall, vm.Screen);
        Assert.Equal("Detecting installed version...", vm.StatusMessage);
        Assert.Equal(1, detectCalls);
    }

    [Fact]
    public async Task InstallerFlowState_Install_WhenFluxoRunningAndUserDeclinesTermination_BlocksBeforeDetect()
    {
        var detectCalls = 0;
        var vm = CreateViewModel(
            requestDetect: () => detectCalls++,
            getRunningFluxoProcessIds: static () => [1234],
            requestTerminateRunningAppConfirmation: _ => false,
            fileExists: static _ => true);

        await vm.InstallCommand.ExecuteAsync(null);

        Assert.Equal(InstallerState.FinishedFailed, vm.State);
        Assert.Equal(InstallerScreen.Finished, vm.Screen);
        Assert.Equal(0, detectCalls);
        Assert.Equal(
            "Installation did not run because fluxo is still open. Please close fluxo and run setup again.",
            vm.StatusMessage);
    }

    [Fact]
    public void InstallerFlowState_ContinueMaintenance_RepairWhenFluxoRunningAndUserDeclinesTermination_BlocksBeforeDetect()
    {
        var detectCalls = 0;
        var processCheckCalls = 0;
        var vm = CreateViewModel(
            requestDetect: () => detectCalls++,
            operationMode: InstallerOperationMode.Maintenance,
            getRunningFluxoProcessIds: () =>
            {
                processCheckCalls++;
                return processCheckCalls == 1 ? [] : [1234];
            },
            requestTerminateRunningAppConfirmation: _ => false,
            fileExists: static _ => true);

        vm.Begin();
        vm.SelectedMaintenanceAction = InstallerMaintenanceAction.Repair;
        vm.ContinueMaintenanceCommand.Execute(null);

        Assert.Equal(InstallerState.FinishedFailed, vm.State);
        Assert.Equal(InstallerScreen.Finished, vm.Screen);
        Assert.Equal(InstallerRequestedOperation.Repair, vm.RequestedOperation);
        Assert.Equal(0, detectCalls);
        Assert.Equal(
            "Repair did not run because fluxo is still open. Please close fluxo and run the repairer again.",
            vm.StatusMessage);
    }

    [Fact]
    public async Task InstallerFlowState_Install_WhenFluxoRunning_RequestsInstallSpecificTerminationConfirmation()
    {
        InstallerRequestedOperation? requestedOperation = null;
        var vm = CreateViewModel(
            getRunningFluxoProcessIds: static () => [1234],
            requestTerminateRunningAppConfirmation: operation =>
            {
                requestedOperation = operation;
                return false;
            },
            fileExists: static _ => true);

        await vm.InstallCommand.ExecuteAsync(null);

        Assert.Equal(InstallerRequestedOperation.Install, requestedOperation);
    }

    [Fact]
    public void InstallerFlowState_ContinueMaintenance_RepairWhenFluxoRunning_RequestsRepairSpecificTerminationConfirmation()
    {
        InstallerRequestedOperation? requestedOperation = null;
        var processCheckCalls = 0;
        var vm = CreateViewModel(
            operationMode: InstallerOperationMode.Maintenance,
            getRunningFluxoProcessIds: () =>
            {
                processCheckCalls++;
                return processCheckCalls == 1 ? [] : [1234];
            },
            requestTerminateRunningAppConfirmation: operation =>
            {
                requestedOperation = operation;
                return false;
            },
            fileExists: static _ => true);

        vm.Begin();
        vm.SelectedMaintenanceAction = InstallerMaintenanceAction.Repair;
        vm.ContinueMaintenanceCommand.Execute(null);

        Assert.Equal(InstallerRequestedOperation.Repair, requestedOperation);
    }

    [Fact]
    public void InstallerFlowState_ContinueMaintenance_UninstallWhenFluxoRunning_RequestsUninstallSpecificTerminationConfirmation()
    {
        InstallerRequestedOperation? requestedOperation = null;
        var processCheckCalls = 0;
        var vm = CreateViewModel(
            operationMode: InstallerOperationMode.Maintenance,
            getRunningFluxoProcessIds: () =>
            {
                processCheckCalls++;
                return processCheckCalls == 1 ? [] : [1234];
            },
            requestTerminateRunningAppConfirmation: operation =>
            {
                requestedOperation = operation;
                return false;
            });

        vm.Begin();
        vm.SelectedMaintenanceAction = InstallerMaintenanceAction.Uninstall;
        vm.ContinueMaintenanceCommand.Execute(null);

        Assert.Equal(InstallerRequestedOperation.Uninstall, requestedOperation);
    }

    [Fact]
    public async Task InstallerFlowState_Install_WhenTerminationFails_BlocksBeforeDetect()
    {
        var detectCalls = 0;
        var terminateCalls = 0;
        var vm = CreateViewModel(
            requestDetect: () => detectCalls++,
            getRunningFluxoProcessIds: static () => [1234],
            requestTerminateRunningAppConfirmation: _ => true,
            tryTerminateProcessById: _ =>
            {
                terminateCalls++;
                return false;
            },
            fileExists: static _ => true);

        await vm.InstallCommand.ExecuteAsync(null);

        Assert.Equal(InstallerState.FinishedFailed, vm.State);
        Assert.Equal(InstallerScreen.Finished, vm.Screen);
        Assert.Equal(0, detectCalls);
        Assert.Equal(1, terminateCalls);
        Assert.Equal(
            "Installation did not run because fluxo could not be terminated. Please close fluxo and run setup again.",
            vm.StatusMessage);
    }

    [Fact]
    public void InstallerFlowState_ContinueMaintenance_RepairWhenTerminationFails_BlocksBeforeDetect()
    {
        var detectCalls = 0;
        var terminateCalls = 0;
        var processCheckCalls = 0;
        var vm = CreateViewModel(
            requestDetect: () => detectCalls++,
            operationMode: InstallerOperationMode.Maintenance,
            getRunningFluxoProcessIds: () =>
            {
                processCheckCalls++;
                return processCheckCalls == 1 ? [] : [1234];
            },
            requestTerminateRunningAppConfirmation: _ => true,
            tryTerminateProcessById: _ =>
            {
                terminateCalls++;
                return false;
            },
            fileExists: static _ => true);

        vm.Begin();
        vm.SelectedMaintenanceAction = InstallerMaintenanceAction.Repair;
        vm.ContinueMaintenanceCommand.Execute(null);

        Assert.Equal(InstallerState.FinishedFailed, vm.State);
        Assert.Equal(InstallerScreen.Finished, vm.Screen);
        Assert.Equal(InstallerRequestedOperation.Repair, vm.RequestedOperation);
        Assert.Equal(0, detectCalls);
        Assert.Equal(1, terminateCalls);
        Assert.Equal(
            "Repair did not run because fluxo could not be terminated. Please close fluxo and run the repairer again.",
            vm.StatusMessage);
    }

    [Fact]
    public void InstallerFlowState_ContinueMaintenance_UninstallWhenFluxoRunningAndUserDeclines_ShowsRetryMessage()
    {
        var detectCalls = 0;
        var processCheckCalls = 0;
        var vm = CreateViewModel(
            requestDetect: () => detectCalls++,
            operationMode: InstallerOperationMode.Maintenance,
            getRunningFluxoProcessIds: () =>
            {
                processCheckCalls++;
                return processCheckCalls == 1 ? [] : [1234];
            },
            requestTerminateRunningAppConfirmation: _ => false);

        vm.Begin();
        vm.SelectedMaintenanceAction = InstallerMaintenanceAction.Uninstall;
        vm.ContinueMaintenanceCommand.Execute(null);

        Assert.Equal(InstallerState.FinishedFailed, vm.State);
        Assert.Equal(InstallerScreen.Finished, vm.Screen);
        Assert.Equal(InstallerRequestedOperation.Uninstall, vm.RequestedOperation);
        Assert.Equal(0, detectCalls);
        Assert.Equal(
            "Uninstallation did not run because fluxo is still open. Please close fluxo and run the repairer again.",
            vm.StatusMessage);
    }

    [Fact]
    public void InstallerFlowState_ContinueMaintenance_UninstallWhenTerminationFails_ShowsRetryMessage()
    {
        var detectCalls = 0;
        var terminateCalls = 0;
        var processCheckCalls = 0;
        var vm = CreateViewModel(
            requestDetect: () => detectCalls++,
            operationMode: InstallerOperationMode.Maintenance,
            getRunningFluxoProcessIds: () =>
            {
                processCheckCalls++;
                return processCheckCalls == 1 ? [] : [1234];
            },
            requestTerminateRunningAppConfirmation: _ => true,
            tryTerminateProcessById: _ =>
            {
                terminateCalls++;
                return false;
            });

        vm.Begin();
        vm.SelectedMaintenanceAction = InstallerMaintenanceAction.Uninstall;
        vm.ContinueMaintenanceCommand.Execute(null);

        Assert.Equal(InstallerState.FinishedFailed, vm.State);
        Assert.Equal(InstallerScreen.Finished, vm.Screen);
        Assert.Equal(InstallerRequestedOperation.Uninstall, vm.RequestedOperation);
        Assert.Equal(0, detectCalls);
        Assert.Equal(1, terminateCalls);
        Assert.Equal(
            "Uninstallation did not run because fluxo could not be terminated. Please close fluxo and run the repairer again.",
            vm.StatusMessage);
    }

    [Fact]
    public void InstallerFlowState_DetectComplete_RepairOperation_RequestsPlan()
    {
        var planCalls = 0;
        var vm = CreateViewModel(
            requestPlan: () => planCalls++,
            operationMode: InstallerOperationMode.Maintenance);

        vm.Begin();
        vm.ContinueMaintenanceCommand.Execute(null);
        vm.OnDetectComplete(0);

        Assert.Equal(InstallerRequestedOperation.Repair, vm.RequestedOperation);
        Assert.Equal("Planning repair...", vm.StatusMessage);
        Assert.Equal(1, planCalls);
    }

    [Fact]
    public void InstallerFlowState_DetectComplete_UninstallMode_RequestsPlan()
    {
        var planCalls = 0;
        var vm = CreateViewModel(
            requestPlan: () => planCalls++,
            operationMode: InstallerOperationMode.Uninstall);

        vm.Begin();
        vm.OnDetectComplete(0);

        Assert.Equal("Planning uninstall...", vm.StatusMessage);
        Assert.Equal(1, planCalls);
    }

    [Fact]
    public void InstallerFlowState_ApplyComplete_SuccessUninstallMode_TransitionsToFinishedUninstalled()
    {
        var deletedFiles = new List<string>();
        var deletedDirectories = new List<string>();
        var deletedRegistrySubKeys = new List<(RegistryView RegistryView, string Path)>();
        var startProcessCalls = 0;
        ProcessStartInfo? deferredCleanupStartInfo = null;
        string? writtenScriptPath = null;
        string? writtenScriptContents = null;
        var runtimeUninstallCalls = 0;
        var installFolder = WindowsPathFixtures.ProgramFilesFluxoFolder;
        var localAppDataFolder = WindowsPathFixtures.LocalAppDataFluxoFolder;
        var staleFilePath = Path.Combine(installFolder, "fluxo.exe");
        var staleDirectoryPath = Path.Combine(installFolder, "cache");
        var repairerPath = Path.Combine(installFolder, "fluxo.Repairer.exe");
        var vm = CreateViewModel(
            operationMode: InstallerOperationMode.Uninstall,
            directoryExists: path =>
            {
                return string.Equals(path, installFolder, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(path, staleDirectoryPath, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(path, localAppDataFolder, StringComparison.OrdinalIgnoreCase);
            },
            enumerateFileSystemEntries: _ => [staleFilePath, staleDirectoryPath, repairerPath],
            deleteDirectory: path => deletedDirectories.Add(path),
            deleteFile: path => deletedFiles.Add(path),
            deleteLocalMachineRegistrySubKeyTree: (registryView, path) =>
                deletedRegistrySubKeys.Add((registryView, path)),
            localApplicationDataFluxoFolder: localAppDataFolder,
            createDeferredCleanupScriptPath: () => WindowsPathFixtures.CleanupScript,
            writeAllText: (path, content) =>
            {
                writtenScriptPath = path;
                writtenScriptContents = content;
            },
            startProcess: startInfo =>
            {
                startProcessCalls++;
                deferredCleanupStartInfo = startInfo;
            },
            runtimeInstaller: new DelegateRuntimeInstaller(
                uninstallOwnedRuntimeAsync: _ =>
                {
                    runtimeUninstallCalls++;
                    return Task.FromResult(new DotNetRuntimeUninstallResult(
                        DotNetRuntimeUninstallStatus.Uninstalled,
                        "removed"));
                }));

        vm.Begin();
        vm.OnApplyComplete(0);

        Assert.Single(deletedFiles);
        Assert.Equal(staleFilePath, deletedFiles[0]);
        Assert.Equal(2, deletedDirectories.Count);
        Assert.Contains(staleDirectoryPath, deletedDirectories);
        Assert.Contains(localAppDataFolder, deletedDirectories);
        Assert.Equal(2, deletedRegistrySubKeys.Count);
        Assert.Contains(
            (RegistryView.Registry64, InstalledVersionRegistryReader.InstalledVersionSubKeyPath),
            deletedRegistrySubKeys);
        Assert.Contains(
            (RegistryView.Registry32, InstalledVersionRegistryReader.InstalledVersionSubKeyPath),
            deletedRegistrySubKeys);
        Assert.Equal(1, startProcessCalls);
        Assert.NotNull(deferredCleanupStartInfo);
        Assert.Equal(Path.GetTempPath(), deferredCleanupStartInfo!.WorkingDirectory);
        Assert.Equal(WindowsPathFixtures.CleanupScript, writtenScriptPath);
        Assert.NotNull(writtenScriptContents);
        Assert.Contains("cd /d \"%TEMP%\"", writtenScriptContents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fluxo.Repairer.exe", writtenScriptContents, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, runtimeUninstallCalls);
        Assert.Equal(InstallerState.FinishedUninstalled, vm.State);
        Assert.Equal(InstallerScreen.Finished, vm.Screen);
        Assert.Equal("fluxo", vm.FinishedTitle);
        Assert.Equal("Thank you for letting fluxo help", vm.FinishedSubtitle);
        Assert.Equal("Uninstallation complete.", vm.StatusMessage);
    }

    [Fact]
    public void InstallerFlowState_ApplyComplete_SuccessUninstallModeFails_WhenOwnedRuntimeUninstallFails()
    {
        var vm = CreateViewModel(
            operationMode: InstallerOperationMode.Uninstall,
            directoryExists: static _ => false,
            runtimeInstaller: new DelegateRuntimeInstaller(
                uninstallOwnedRuntimeAsync: _ => Task.FromResult(new DotNetRuntimeUninstallResult(
                    DotNetRuntimeUninstallStatus.Failed,
                    "uninstaller exited with code 123"))));

        vm.Begin();
        vm.OnApplyComplete(0);

        Assert.Equal(InstallerState.FinishedFailed, vm.State);
        Assert.Equal(
            "Uninstallation cleanup failed: uninstaller exited with code 123",
            vm.StatusMessage);
    }

    [Fact]
    public void InstallerFlowState_MaintenanceUninstall_UsesRepairerParentDirectory_ForCleanupTarget()
    {
        var deletedFiles = new List<string>();
        var deletedDirectories = new List<string>();
        string? writtenScriptContents = null;
        var installFolder = WindowsPathFixtures.AppsFluxoFolderWithCapitalName;
        var staleFilePath = Path.Combine(installFolder, "fluxo.exe");
        var staleDirectoryPath = Path.Combine(installFolder, "cache");
        var repairerPath = Path.Combine(installFolder, "fluxo.Repairer.exe");
        var vm = new InstallerViewModel(
            dotNetRuntimeDetector: new FixedRuntimeDetector(true),
            requestDetect: static () => { },
            directoryExists: path =>
                string.Equals(path, installFolder, StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, staleDirectoryPath, StringComparison.OrdinalIgnoreCase),
            enumerateFileSystemEntries: _ => [staleFilePath, staleDirectoryPath, repairerPath],
            deleteDirectory: path => deletedDirectories.Add(path),
            deleteFile: path => deletedFiles.Add(path),
            createDeferredCleanupScriptPath: () => WindowsPathFixtures.CleanupScript,
            writeAllText: (_, content) => writtenScriptContents = content,
            startProcess: _ => { },
            getRunningFluxoProcessIds: static () => [],
            requestUninstallConfirmation: static () => true,
            operationMode: InstallerOperationMode.Maintenance,
            bundleExecutablePath: repairerPath,
            copyFile: static (_, _, _) => { });

        vm.Begin();
        vm.SelectedMaintenanceAction = InstallerMaintenanceAction.Uninstall;
        vm.ContinueMaintenanceCommand.Execute(null);
        vm.OnApplyComplete(0);

        Assert.Single(deletedFiles);
        Assert.Equal(staleFilePath, deletedFiles[0]);
        Assert.Single(deletedDirectories);
        Assert.Equal(staleDirectoryPath, deletedDirectories[0]);
        Assert.NotNull(writtenScriptContents);
        Assert.Contains(installFolder, writtenScriptContents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FLUXO_DIR", writtenScriptContents, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstallerFlowState_ContinueMaintenance_UninstallWhenUninstallConfirmationDeclined_CancelsWithoutStarting()
    {
        var detectCalls = 0;
        var vm = CreateViewModel(
            requestDetect: () => detectCalls++,
            operationMode: InstallerOperationMode.Maintenance,
            requestUninstallConfirmation: () => false);

        vm.Begin();
        vm.SelectedMaintenanceAction = InstallerMaintenanceAction.Uninstall;
        vm.ContinueMaintenanceCommand.Execute(null);

        Assert.Equal(InstallerState.Welcome, vm.State);
        Assert.Equal(InstallerScreen.AppFound, vm.Screen);
        Assert.Equal(InstallerRequestedOperation.Install, vm.RequestedOperation);
        Assert.Equal("Uninstallation cancelled.", vm.StatusMessage);
        Assert.Equal(0, detectCalls);
    }

    [Fact]
    public void InstallerFlowState_Begin_UninstallModeWhenUninstallConfirmationDeclined_TransitionsToFinishedCancelled()
    {
        var detectCalls = 0;
        var vm = CreateViewModel(
            requestDetect: () => detectCalls++,
            operationMode: InstallerOperationMode.Uninstall,
            requestUninstallConfirmation: () => false);

        vm.Begin();

        Assert.Equal(InstallerState.FinishedCancelled, vm.State);
        Assert.Equal(InstallerScreen.Finished, vm.Screen);
        Assert.Equal("Uninstallation cancelled.", vm.StatusMessage);
        Assert.Equal(0, detectCalls);
    }

    [Fact]
    public void InstallerFlowState_ApplyComplete_FailureMaintenanceUninstall_ShowsUninstallFailureCopy()
    {
        var vm = CreateViewModel(operationMode: InstallerOperationMode.Maintenance);

        vm.Begin();
        vm.SelectedMaintenanceAction = InstallerMaintenanceAction.Uninstall;
        vm.ContinueMaintenanceCommand.Execute(null);
        vm.OnApplyComplete(1);

        Assert.Equal(InstallerState.FinishedFailed, vm.State);
        Assert.Equal("Uninstallation failed", vm.FinishedTitle);
        Assert.StartsWith("Uninstallation failed.", vm.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallerFlowState_ApplyComplete_SuccessUninstallFails_WhenDeferredCleanupCannotBeScheduled()
    {
        var vm = CreateViewModel(
            operationMode: InstallerOperationMode.Uninstall,
            directoryExists: static _ => true,
            enumerateFileSystemEntries: static _ => [],
            writeAllText: static (_, _) => throw new IOException("Access denied."));

        vm.Begin();
        vm.OnApplyComplete(0);

        Assert.Equal(InstallerState.FinishedFailed, vm.State);
        Assert.StartsWith("Uninstallation failed:", vm.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallerFlowState_ApplyComplete_Success_CopiesRepairerExecutable()
    {
        string? copiedSource = null;
        string? copiedDestination = null;
        bool? copiedOverwrite = null;

        var vm = new InstallerViewModel(
            dotNetRuntimeDetector: new FixedRuntimeDetector(true),
            fileExists: static _ => true,
            bundleExecutablePath: WindowsPathFixtures.DefaultInstaller,
            copyFile: (source, destination, overwrite) =>
            {
                copiedSource = source;
                copiedDestination = destination;
                copiedOverwrite = overwrite;
            });

        vm.OnApplyComplete(0);

        Assert.Equal(WindowsPathFixtures.DefaultInstaller, copiedSource);
        Assert.Equal(WindowsPathFixtures.RepairerExecutable, copiedDestination);
        Assert.True(copiedOverwrite);
        Assert.Equal(InstallerState.FinishedSuccess, vm.State);
    }

    [Fact]
    public void InstallerFlowState_ApplyComplete_Success_DoesNotCopyRepairerOverItself()
    {
        var copyCalls = 0;
        var vm = new InstallerViewModel(
            dotNetRuntimeDetector: new FixedRuntimeDetector(true),
            fileExists: static _ => true,
            bundleExecutablePath: WindowsPathFixtures.RepairerExecutable,
            copyFile: (_, _, _) =>
            {
                copyCalls++;
                throw new InvalidOperationException("Copy should not be called.");
            });

        vm.OnApplyComplete(0);

        Assert.Equal(0, copyCalls);
        Assert.Equal(InstallerState.FinishedSuccess, vm.State);
    }

    [Fact]
    public void InstallerFlowState_ApplyComplete_SuccessTransitionsToFailedWhen_ExeMissing()
    {
        var vm = CreateViewModel(fileExists: static _ => false);

        vm.OnApplyComplete(0);

        Assert.Equal(InstallerState.FinishedFailed, vm.State);
        Assert.Equal("Verification failed: fluxo.exe was not found.", vm.StatusMessage);
        Assert.Equal(1, vm.ExitCode);
    }

    [Fact]
    public void InstallerFlowState_ExitCode_DefaultsToCancelWhen_NotInTerminalState()
    {
        var vm = CreateViewModel(fileExists: static _ => true);

        Assert.Equal(1602, vm.ExitCode);
    }

    private static InstallerViewModel CreateViewModel(
        Action<string>? setInstallFolderVariable = null,
        Action? requestDetect = null,
        Action? requestPlan = null,
        Action? requestApply = null,
        IDotNetRuntimeInstaller? runtimeInstaller = null,
        ILegacySelfContainedCleanupService? legacyCleanupService = null,
        Func<string, bool>? fileExists = null,
        Func<bool>? requestRollback = null,
        Func<bool>? requestCancelConfirmation = null,
        Func<bool>? requestUninstallConfirmation = null,
        InstallerOperationMode operationMode = InstallerOperationMode.Install,
        Func<string, bool>? directoryExists = null,
        Func<string, string[]>? enumerateFileSystemEntries = null,
        Action<string>? deleteDirectory = null,
        Action<string>? deleteFile = null,
        Action<RegistryView, string>? deleteLocalMachineRegistrySubKeyTree = null,
        string? localApplicationDataFluxoFolder = null,
        Func<string>? createDeferredCleanupScriptPath = null,
        Action<string, string>? writeAllText = null,
        Action<ProcessStartInfo>? startProcess = null,
        Func<IReadOnlyList<int>>? getRunningFluxoProcessIds = null,
        Func<int, bool>? tryTerminateProcessById = null,
        Func<InstallerRequestedOperation, bool>? requestTerminateRunningAppConfirmation = null,
        Action? closeInstallerAction = null)
    {
        return new InstallerViewModel(
            dotNetRuntimeDetector: new FixedRuntimeDetector(true),
            runtimeInstaller: runtimeInstaller,
            legacyCleanupService: legacyCleanupService,
            setInstallFolderVariable: setInstallFolderVariable,
            requestDetect: requestDetect,
            requestPlan: requestPlan,
            requestApply: requestApply,
            fileExists: fileExists,
            requestRollback: requestRollback,
            requestCancelConfirmation: requestCancelConfirmation,
            requestUninstallConfirmation: requestUninstallConfirmation ?? (static () => true),
            directoryExists: directoryExists,
            enumerateFileSystemEntries: enumerateFileSystemEntries,
            deleteDirectory: deleteDirectory,
            deleteFile: deleteFile,
            deleteLocalMachineRegistrySubKeyTree: deleteLocalMachineRegistrySubKeyTree,
            localApplicationDataFluxoFolder: localApplicationDataFluxoFolder,
            createDeferredCleanupScriptPath: createDeferredCleanupScriptPath,
            writeAllText: writeAllText,
            startProcess: startProcess,
            getRunningFluxoProcessIds: getRunningFluxoProcessIds ?? (static () => []),
            tryTerminateProcessById: tryTerminateProcessById,
            requestTerminateRunningAppConfirmation: requestTerminateRunningAppConfirmation,
            operationMode: operationMode,
            bundleExecutablePath: WindowsPathFixtures.DefaultInstaller,
            copyFile: static (_, _, _) => { },
            closeInstallerAction: closeInstallerAction);
    }

    private sealed class FixedRuntimeDetector(bool isInstalled) : IDotNetRuntimeDetector
    {
        public bool IsRequiredRuntimeInstalled() => isInstalled;
    }

    private sealed class DelegateRuntimeInstaller(
        Func<CancellationToken, Task<DotNetRuntimeInstallResult>>? ensureInstalledAsync = null,
        Func<CancellationToken, Task>? rollbackRuntimeInstalledByFluxoAsync = null,
        Func<CancellationToken, Task<DotNetRuntimeUninstallResult>>? uninstallOwnedRuntimeAsync = null,
        Action? requestCancellation = null,
        Action? cleanupDownloadedInstaller = null)
        : IDotNetRuntimeInstaller
    {
        public Task<DotNetRuntimeInstallResult> EnsureInstalledAsync(CancellationToken cancellationToken) =>
            ensureInstalledAsync?.Invoke(cancellationToken)
            ?? Task.FromResult(new DotNetRuntimeInstallResult(
                DotNetRuntimeInstallStatus.AlreadyInstalled,
                "already installed"));

        public Task RollbackRuntimeInstalledByFluxoAsync(CancellationToken cancellationToken) =>
            rollbackRuntimeInstalledByFluxoAsync?.Invoke(cancellationToken) ?? Task.CompletedTask;

        public Task<DotNetRuntimeUninstallResult> UninstallOwnedRuntimeAsync(CancellationToken cancellationToken) =>
            uninstallOwnedRuntimeAsync?.Invoke(cancellationToken)
            ?? Task.FromResult(new DotNetRuntimeUninstallResult(
                DotNetRuntimeUninstallStatus.Skipped,
                "skipped"));

        public void RequestCancellation() => requestCancellation?.Invoke();

        public void CleanupDownloadedInstaller() => cleanupDownloadedInstaller?.Invoke();
    }

    private sealed class DelegateLegacyCleanupService(Func<string, LegacyCleanupResult> cleanup)
        : ILegacySelfContainedCleanupService
    {
        public LegacyCleanupResult Cleanup(string installFolder) => cleanup(installFolder);
    }
}
