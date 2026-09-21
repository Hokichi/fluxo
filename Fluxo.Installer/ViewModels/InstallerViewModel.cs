using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluxo.Installer.Models;
using Fluxo.Installer.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using Fluxo.Resources.CustomControls;

namespace Fluxo.Installer.ViewModels;

public partial class InstallerViewModel : ObservableObject
{
    private const int SuccessStatus = 0;
    private const string InstallingChecklistLabel = "Installing";
    private const string RepairingChecklistLabel = "Repairing";
    private const string InstalledExecutableName = "fluxo.exe";
    private const string RepairerExecutableName = "fluxo.Repairer.exe";
    private const string FluxoFolderName = "fluxo";
    private const string CleanupScriptPrefix = "fluxo-cleanup-";
    private const int DeferredCleanupRetryCount = 60;
    private const int DeferredCleanupRetryDelaySeconds = 1;
    private const int ProcessTerminationTimeoutMilliseconds = 5000;
    private const int SuccessExitCode = 0;
    private const int CancelExitCode = 1602;
    private const int FailureExitCode = 1;

    private readonly Action<string> setInstallFolderVariable;
    private readonly Action requestDetect;
    private readonly Action requestPlan;
    private readonly Action requestApply;
    private readonly Func<bool> requestRollback;
    private readonly Func<bool> requestCancelConfirmation;
    private readonly Func<bool> requestUninstallConfirmation;
    private readonly bool isRollbackConfigured;
    private readonly Func<string, bool> fileExists;
    private readonly Func<string, bool> directoryExists;
    private readonly Func<string, string[]> enumerateFileSystemEntries;
    private readonly Action<string> deleteDirectory;
    private readonly Action<string> deleteFile;
    private readonly Action<RegistryView, string> deleteLocalMachineRegistrySubKeyTree;
    private readonly string localApplicationDataFluxoFolder;
    private readonly Func<string> createDeferredCleanupScriptPath;
    private readonly Action<string, string> writeAllText;
    private readonly Action<ProcessStartInfo> startProcess;
    private readonly Func<IReadOnlyList<int>> getRunningFluxoProcessIds;
    private readonly Func<int, bool> tryTerminateProcessById;
    private readonly Func<InstallerRequestedOperation, bool> requestTerminateRunningAppConfirmation;
    private readonly Action<string> launchInstalledApp;
    private readonly Action<string, string, bool> copyFile;
    private readonly IDotNetRuntimeInstaller runtimeInstaller;
    private readonly ILegacySelfContainedCleanupService legacyCleanupService;
    private readonly InstallerOperationMode operationMode;
    private readonly string bundleExecutablePath;
    private readonly bool hasConstructorCloseInstallerAction;
    private Action closeInstallerAction = () => { };

    private readonly InstallerChecklistStep prerequisitesChecklistStep = new("Checking prerequisites");
    private readonly InstallerChecklistStep installingChecklistStep = new(InstallingChecklistLabel);
    private readonly InstallerChecklistStep cleanUpChecklistStep = new("Cleaning up");
    private readonly InstallerChecklistStep rollbackChecklistStep = new("Rolling back");

    private bool installFolderExistedBeforeInstall;
    private bool installStarted;
    private bool startupDetectionStarted;
    private bool startupDetectionFailed;
    private bool hasExistingInstallation;
    private bool planRequested;
    private string installFolderForCurrentRun = string.Empty;

    public InstallerViewModel(
        IDotNetRuntimeDetector? dotNetRuntimeDetector = null,
        IDotNetRuntimeInstaller? runtimeInstaller = null,
        ILegacySelfContainedCleanupService? legacyCleanupService = null,
        Action<string>? setInstallFolderVariable = null,
        Action? requestDetect = null,
        Action? requestPlan = null,
        Action? requestApply = null,
        Func<string, bool>? fileExists = null,
        Action<string>? launchInstalledApp = null,
        Func<bool>? requestRollback = null,
        Func<bool>? requestCancelConfirmation = null,
        Func<bool>? requestUninstallConfirmation = null,
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
        InstallerOperationMode operationMode = InstallerOperationMode.Install,
        string? bundleExecutablePath = null,
        Action<string, string, bool>? copyFile = null,
        Action? closeInstallerAction = null,
        string? requestedInstallFolder = null)
    {
        this.runtimeInstaller = runtimeInstaller
            ?? new DotNetRuntimeInstaller(dotNetRuntimeDetector ?? new DotNetRuntimeDetector());
        this.legacyCleanupService = legacyCleanupService ?? new LegacySelfContainedCleanupService();
        this.setInstallFolderVariable = setInstallFolderVariable ?? (_ => { });
        this.requestDetect = requestDetect ?? (() => { });
        this.requestPlan = requestPlan ?? (() => { });
        this.requestApply = requestApply ?? (() => { });
        this.requestRollback = requestRollback ?? (() => false);
        this.requestCancelConfirmation = requestCancelConfirmation ?? ShowCancelConfirmationMessage;
        this.requestUninstallConfirmation = requestUninstallConfirmation ?? ShowUninstallConfirmationMessage;
        isRollbackConfigured = requestRollback is not null;
        this.fileExists = fileExists ?? File.Exists;
        this.directoryExists = directoryExists ?? Directory.Exists;
        this.enumerateFileSystemEntries = enumerateFileSystemEntries
            ?? (path => Directory.EnumerateFileSystemEntries(path).ToArray());
        this.deleteDirectory = deleteDirectory ?? (path => Directory.Delete(path, recursive: true));
        this.deleteFile = deleteFile ?? File.Delete;
        this.deleteLocalMachineRegistrySubKeyTree = deleteLocalMachineRegistrySubKeyTree
            ?? DeleteLocalMachineRegistrySubKeyTree;
        this.localApplicationDataFluxoFolder = localApplicationDataFluxoFolder
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                FluxoFolderName);
        this.createDeferredCleanupScriptPath = createDeferredCleanupScriptPath
            ?? (() => Path.Combine(
                Path.GetTempPath(),
                $"{CleanupScriptPrefix}{Guid.NewGuid():N}.cmd"));
        this.writeAllText = writeAllText ?? File.WriteAllText;
        this.startProcess = startProcess ?? (startInfo => _ = Process.Start(startInfo));
        this.getRunningFluxoProcessIds = getRunningFluxoProcessIds ?? GetRunningFluxoProcessIds;
        this.tryTerminateProcessById = tryTerminateProcessById ?? TryTerminateProcessById;
        this.requestTerminateRunningAppConfirmation = requestTerminateRunningAppConfirmation
            ?? ShowTerminateRunningAppConfirmation;
        this.launchInstalledApp = launchInstalledApp ?? LaunchInstalledApp;
        this.operationMode = operationMode;
        this.bundleExecutablePath = bundleExecutablePath ?? string.Empty;
        this.copyFile = copyFile ?? ((source, destination, overwrite) => File.Copy(source, destination, overwrite));
        hasConstructorCloseInstallerAction = closeInstallerAction is not null;
        this.closeInstallerAction = closeInstallerAction ?? (() => { });
        if (IsValidInstallFolder(requestedInstallFolder))
        {
            InstallFolder = requestedInstallFolder!;
        }

        ChecklistSteps = new ObservableCollection<InstallerChecklistStep>(
        [
            prerequisitesChecklistStep,
            installingChecklistStep,
            cleanUpChecklistStep
        ]);
    }

    [ObservableProperty]
    private string title = "fluxo";

    [ObservableProperty]
    private string tagline = "Your Finances All In One Place";

    [ObservableProperty]
    private string installFolder = @"C:\Program Files\fluxo";

    [ObservableProperty]
    private InstallerState state = InstallerState.Welcome;

    [ObservableProperty]
    private InstallerScreen screen = InstallerScreen.Welcome;

    private string? upToDateInstalledVersion;
    private bool upToDateInstalledVersionIsNewer;

    public ObservableCollection<InstallerChecklistStep> ChecklistSteps { get; }

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private InstallerMaintenanceAction selectedMaintenanceAction = InstallerMaintenanceAction.Repair;

    public InstallerRequestedOperation RequestedOperation { get; private set; } = InstallerRequestedOperation.Install;

    public bool IsMaintenanceMode =>
        operationMode == InstallerOperationMode.Maintenance
        || InstallerOperationModeDetector.Detect(bundleExecutablePath, null) == InstallerOperationMode.Maintenance;

    public bool IsUninstallMode => operationMode == InstallerOperationMode.Uninstall;

    public bool IsRepairSelected
    {
        get => SelectedMaintenanceAction == InstallerMaintenanceAction.Repair;
        set
        {
            if (value)
            {
                SelectedMaintenanceAction = InstallerMaintenanceAction.Repair;
            }
        }
    }

    public bool IsUninstallSelected
    {
        get => SelectedMaintenanceAction == InstallerMaintenanceAction.Uninstall;
        set
        {
            if (value)
            {
                SelectedMaintenanceAction = InstallerMaintenanceAction.Uninstall;
            }
        }
    }

    public string FinishedTitle => State switch
    {
        InstallerState.FinishedSuccess => "Let's begin",
        InstallerState.FinishedUpToDate => "Let's begin",
        InstallerState.FinishedUninstalled => "fluxo",
        InstallerState.FinishedCancelled => "Installation cancelled",
        InstallerState.FinishedFailed when RequestedOperation == InstallerRequestedOperation.Uninstall => "Uninstallation failed",
        InstallerState.FinishedFailed when RequestedOperation == InstallerRequestedOperation.Repair => "Repair failed",
        _ => "Installation failed",
    };

    public string FinishedSubtitle => State switch
    {
        InstallerState.FinishedSuccess => "Your finance, simplified",
        InstallerState.FinishedUpToDate when upToDateInstalledVersionIsNewer
            && !string.IsNullOrWhiteSpace(upToDateInstalledVersion)
                => $"Newer version found: {upToDateInstalledVersion}",
        InstallerState.FinishedUpToDate => "Version is up-to-date.",
        InstallerState.FinishedUninstalled => "Thank you for letting fluxo help",
        _ => "Please close the setup and run it again",
    };

    public int ExitCode
    {
        get
        {
            return State switch
            {
                InstallerState.FinishedSuccess => SuccessExitCode,
                InstallerState.FinishedUpToDate => SuccessExitCode,
                InstallerState.FinishedUninstalled => SuccessExitCode,
                InstallerState.FinishedFailed => FailureExitCode,
                InstallerState.FinishedCancelled => CancelExitCode,
                _ => CancelExitCode,
            };
        }
    }

    public void Begin()
    {
        if (!IsMaintenanceMode && !IsUninstallMode)
        {
            if (startupDetectionStarted) return;
            startupDetectionStarted = true;
            State = InstallerState.Detecting;
            StatusMessage = "Checking existing installation...";
            try
            {
                requestDetect();
            }
            catch (Exception ex)
            {
                startupDetectionFailed = true;
                TransitionToFailure($"Could not detect the installed application. Close setup and try again. {ex.Message}");
            }
            return;
        }

        if (!EnsureFluxoCanBeStoppedForOperation(GetStartupPreflightOperation()))
        {
            return;
        }

        if (IsMaintenanceMode)
        {
            StartMaintenance();
            return;
        }

        if (!IsUninstallMode)
        {
            return;
        }

        StartUninstall();
    }

    public string ReinstallPrompt => $"fluxo {upToDateInstalledVersion} is already installed. Do you want to reinstall it?";

    public bool CanEditInstallFolder => !hasExistingInstallation && CanChangeDirectory();

    public bool IsInstallFolderReadOnly => !CanEditInstallFolder;

    public void OnInstallationDetected(int status, InstallerUpToDateDecisionResult decision, string installFolder)
    {
        if (Screen == InstallerScreen.Finished || planRequested) return;
        if (State == InstallerState.Detecting)
        {
            OnStartupDetectionComplete(status, decision, installFolder);
            return;
        }
        if (status == SuccessStatus && decision.IsNewerVersion
            && RequestedOperation != InstallerRequestedOperation.Uninstall)
        {
            OnDetectedUpToDateVersion(decision.InstalledVersion, true, installFolder);
            return;
        }
        if (status == SuccessStatus && decision.RequiresReinstallConfirmation
            && RequestedOperation == InstallerRequestedOperation.Install)
        {
            // An installation can appear between startup discovery and the install click.
            installStarted = false;
            State = InstallerState.Detecting;
            OnStartupDetectionComplete(status, decision, installFolder);
            return;
        }
        OnDetectComplete(status);
    }

    public void OnStartupDetectionComplete(int status, InstallerUpToDateDecisionResult decision, string installFolder)
    {
        if (State != InstallerState.Detecting) return;
        if (status != SuccessStatus)
        {
            startupDetectionFailed = true;
            TransitionToFailure("Could not detect the installed application. Close setup and try again.");
            return;
        }

        InstallFolder = installFolder;
        hasExistingInstallation = !string.IsNullOrWhiteSpace(decision.InstalledVersion);
        upToDateInstalledVersion = decision.InstalledVersion;
        OnPropertyChanged(nameof(ReinstallPrompt));
        if (decision.IsNewerVersion)
        {
            OnDetectedUpToDateVersion(decision.InstalledVersion, true, installFolder);
        }
        else if (decision.RequiresReinstallConfirmation)
        {
            installFolderForCurrentRun = installFolder;
            Screen = InstallerScreen.ReinstallConfirmation;
            State = InstallerState.AwaitingReinstallConfirmation;
            StatusMessage = string.Empty;
        }
        else
        {
            Screen = InstallerScreen.Welcome;
            State = InstallerState.Welcome;
            StatusMessage = hasExistingInstallation ? "Existing installation found." : string.Empty;
        }
    }

    private bool CanConfirmReinstall() => State == InstallerState.AwaitingReinstallConfirmation;

    [RelayCommand(CanExecute = nameof(CanConfirmReinstall))]
    private void DeclineReinstall()
    {
        if (!CanConfirmReinstall()) return;
        OnDetectedUpToDateVersion(upToDateInstalledVersion, installFolder: InstallFolder);
    }

    [RelayCommand(CanExecute = nameof(CanConfirmReinstall))]
    private async Task ReinstallAsync(CancellationToken cancellationToken)
    {
        if (!CanConfirmReinstall()) return;
        RequestedOperation = InstallerRequestedOperation.Repair;
        State = InstallerState.Installing;
        Screen = InstallerScreen.Progress;
        try
        {
            if (!EnsureFluxoCanBeStoppedForOperation(RequestedOperation)) return;
            StatusMessage = "Checking .NET Desktop Runtime...";
            var result = await runtimeInstaller.EnsureInstalledAsync(cancellationToken);
            if (State != InstallerState.Installing) return;
            if (result.Status is DotNetRuntimeInstallStatus.Failed or DotNetRuntimeInstallStatus.Cancelled)
            {
                TransitionToFailure($"Runtime installation failed: {result.Message}");
                return;
            }
            StartRepair();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The cancel action already owns the terminal state.
        }
        catch (Exception ex)
        {
            if (State != InstallerState.FinishedCancelled)
                TransitionToFailure($"Reinstallation failed: {ex.Message}");
        }
        finally
        {
            if (cancellationToken.IsCancellationRequested)
                runtimeInstaller.CleanupDownloadedInstaller();
        }
    }

    [RelayCommand(CanExecute = nameof(CanChangeDirectory))]
    private void ChangeDirectory()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Install Location",
            InitialDirectory = Directory.Exists(InstallFolder) ? InstallFolder : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        };

        if (dialog.ShowDialog() == true)
        {
            InstallFolder = dialog.FolderName;
            StatusMessage = string.Empty;
            return;
        }

        StatusMessage = "Install location unchanged.";
    }

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task Install()
    {
        if (!CanInstall())
        {
            return;
        }

        RequestedOperation = InstallerRequestedOperation.Install;
        planRequested = false;
        Screen = InstallerScreen.Progress;
        State = InstallerState.Installing;
        installStarted = false;
        installFolderExistedBeforeInstall = false;
        installFolderForCurrentRun = string.Empty;
        installingChecklistStep.Label = InstallingChecklistLabel;
        EnsureDefaultChecklistSteps();
        ResetChecklistStates();
        prerequisitesChecklistStep.State = ChecklistStepState.Running;

        if (!IsValidInstallFolder(InstallFolder))
        {
            prerequisitesChecklistStep.State = ChecklistStepState.Failed;
            TransitionToPrerequisitesFailure("A valid install folder is required.");
            return;
        }

        prerequisitesChecklistStep.State = ChecklistStepState.Success;
        if (!EnsureFluxoCanBeStoppedForOperation(InstallerRequestedOperation.Install))
        {
            return;
        }

        installingChecklistStep.State = ChecklistStepState.Running;
        installFolderExistedBeforeInstall = directoryExists(InstallFolder);
        installFolderForCurrentRun = InstallFolder;
        installStarted = true;
        setInstallFolderVariable(GetInstallFolderForCurrentRun());

        StatusMessage = "Checking .NET Desktop Runtime...";
        var runtimeResult = await runtimeInstaller.EnsureInstalledAsync(CancellationToken.None).ConfigureAwait(true);
        if (runtimeResult.Status is DotNetRuntimeInstallStatus.Failed or DotNetRuntimeInstallStatus.Cancelled)
        {
            HandlePostStartFailure($"Runtime installation failed: {runtimeResult.Message}");
            return;
        }

        StatusMessage = "Detecting installation state...";
        requestDetect();
    }

    public void OnDetectComplete(int status)
    {
        if (planRequested || Screen == InstallerScreen.Finished
            || State is InstallerState.Detecting or InstallerState.AwaitingReinstallConfirmation) return;
        if (status != SuccessStatus)
        {
            TransitionToFailure("Could not detect the installed application. Close setup and try again.");
            return;
        }
        planRequested = true;
        if (RequestedOperation == InstallerRequestedOperation.Uninstall)
        {
            StatusMessage = "Planning uninstall...";
            requestPlan();
            return;
        }

        if (RequestedOperation == InstallerRequestedOperation.Repair)
        {
            setInstallFolderVariable(GetInstallFolderForCurrentRun());
            StatusMessage = "Planning repair...";
            requestPlan();
            return;
        }

        if (string.IsNullOrWhiteSpace(installFolderForCurrentRun))
        {
            setInstallFolderVariable(GetInstallFolderForCurrentRun());
        }

        StatusMessage = "Planning installation...";
        requestPlan();
    }

    public void OnDetectedUpToDateVersion(
        string? installedVersion = null,
        bool isNewerVersion = false,
        string? installFolder = null)
    {
        upToDateInstalledVersion = installedVersion;
        upToDateInstalledVersionIsNewer = isNewerVersion;
        if (!string.IsNullOrWhiteSpace(installFolder) && IsValidInstallFolder(installFolder))
        {
            InstallFolder = installFolder;
            installFolderForCurrentRun = installFolder;
        }

        prerequisitesChecklistStep.State = ChecklistStepState.Success;
        installingChecklistStep.State = ChecklistStepState.Success;
        cleanUpChecklistStep.State = ChecklistStepState.Success;
        Screen = InstallerScreen.Finished;
        State = InstallerState.FinishedUpToDate;
        StatusMessage = "Detected installed version is up-to-date.";
    }

    public void OnPlanComplete(int status)
    {
        if (status != SuccessStatus)
        {
            TransitionToFailure("Planning failed.");
            return;
        }

        if (RequestedOperation == InstallerRequestedOperation.Uninstall)
        {
            State = InstallerState.Installing;
            StatusMessage = "Uninstalling files...";
            requestApply();
            return;
        }

        if (RequestedOperation == InstallerRequestedOperation.Repair)
        {
            setInstallFolderVariable(GetInstallFolderForCurrentRun());
            State = InstallerState.Installing;
            StatusMessage = "Repairing files...";
            requestApply();
            return;
        }

        setInstallFolderVariable(GetInstallFolderForCurrentRun());
        State = InstallerState.Installing;
        StatusMessage = "Installing files...";
        requestApply();
    }

    public void OnApplyComplete(int status)
    {
        if (status != SuccessStatus)
        {
            TransitionToFailure(RequestedOperation switch
            {
                InstallerRequestedOperation.Uninstall => "Uninstallation failed.",
                InstallerRequestedOperation.Repair => "Repair failed.",
                _ => "Installation failed.",
            });
            return;
        }

        if (RequestedOperation == InstallerRequestedOperation.Uninstall)
        {
            State = InstallerState.Verifying;
            StatusMessage = "Finalizing uninstallation...";
            CompleteUninstall();
            return;
        }

        State = InstallerState.Verifying;
        StatusMessage = "Cleaning up...";
        CleanUpAfterSuccessfulInstallOrRepair();
    }

    private void CleanUpAfterSuccessfulInstallOrRepair()
    {
        cleanUpChecklistStep.State = ChecklistStepState.Running;
        var cleanupResult = legacyCleanupService.Cleanup(GetInstallFolderForCurrentRun());
        runtimeInstaller.CleanupDownloadedInstaller();
        if (!cleanupResult.Success)
        {
            cleanUpChecklistStep.State = ChecklistStepState.Failed;
            TransitionToFailure($"Cleanup failed: {cleanupResult.Message}");
            return;
        }

        cleanUpChecklistStep.State = ChecklistStepState.Success;
        StatusMessage = "Verifying installation...";
        VerifyInstallation();
    }

    private void VerifyInstallation()
    {
        var installedExePath = Path.Combine(GetInstallFolderForCurrentRun(), InstalledExecutableName);
        if (fileExists(installedExePath))
        {
            if (!EnsureRepairerExecutable(out var repairerError))
            {
                TransitionToFailure(repairerError);
                return;
            }

            installingChecklistStep.State = ChecklistStepState.Success;
            Screen = InstallerScreen.Finished;
            State = InstallerState.FinishedSuccess;
            StatusMessage = RequestedOperation == InstallerRequestedOperation.Repair
                ? "Repair complete."
                : "Installation complete.";
            return;
        }

        TransitionToFailure("Verification failed: fluxo.exe was not found.");
    }

    private void TransitionToFailure(string message)
    {
        if (installStarted)
        {
            HandlePostStartFailure(message);
            return;
        }

        if (prerequisitesChecklistStep.State == ChecklistStepState.Running)
        {
            prerequisitesChecklistStep.State = ChecklistStepState.Failed;
        }

        Screen = InstallerScreen.Finished;
        State = InstallerState.FinishedFailed;
        StatusMessage = message;
    }

    private void TransitionToPrerequisitesFailure(string message)
    {
        Screen = InstallerScreen.Finished;
        State = InstallerState.FinishedFailed;
        installFolderForCurrentRun = string.Empty;
        StatusMessage = message;
    }

    private bool CanInstall() =>
        !startupDetectionFailed &&
        !IsMaintenanceMode &&
        !IsUninstallMode &&
        (State == InstallerState.Welcome || State == InstallerState.FinishedFailed) &&
        IsValidInstallFolder(InstallFolder);

    private bool CanChangeDirectory() =>
        !hasExistingInstallation && !startupDetectionFailed &&
        !IsMaintenanceMode &&
        !IsUninstallMode &&
        (State == InstallerState.Welcome || State == InstallerState.FinishedFailed);

    [RelayCommand(CanExecute = nameof(CanLaunchApp))]
    private void LaunchApp()
    {
        if (State != InstallerState.FinishedSuccess && State != InstallerState.FinishedUpToDate)
        {
            return;
        }

        try
        {
            var installedExePath = Path.Combine(GetInstallFolderForCurrentRun(), InstalledExecutableName);
            if (!fileExists(installedExePath))
            {
                StatusMessage = "Fluxo executable was not found. Setup will stay open.";
                return;
            }

            launchInstalledApp(installedExePath);
            StatusMessage = "Launching Fluxo...";
            closeInstallerAction();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Launch reported an error: {ex.Message}";
        }
    }

    private bool CanLaunchApp() =>
        RequestedOperation != InstallerRequestedOperation.Uninstall &&
        (State == InstallerState.FinishedSuccess || State == InstallerState.FinishedUpToDate);

    [RelayCommand]
    private void ContinueMaintenance()
    {
        if (!IsMaintenanceMode)
        {
            return;
        }

        if (Screen != InstallerScreen.AppFound)
        {
            return;
        }

        if (SelectedMaintenanceAction == InstallerMaintenanceAction.Uninstall)
        {
            RequestedOperation = InstallerRequestedOperation.Uninstall;
            StartUninstall();
            return;
        }

        RequestedOperation = InstallerRequestedOperation.Repair;
        if (!EnsureFluxoCanBeStoppedForOperation(RequestedOperation))
        {
            return;
        }

        StartRepair();
    }

    private void StartRepair()
    {
        planRequested = false;
        installingChecklistStep.Label = RepairingChecklistLabel;
        Screen = InstallerScreen.Progress;
        State = InstallerState.Installing;
        installStarted = true;
        installFolderExistedBeforeInstall = true;
        installFolderForCurrentRun = ResolveInstallFolderForCurrentRun();
        EnsureDefaultChecklistSteps();
        ResetChecklistStates();
        prerequisitesChecklistStep.State = ChecklistStepState.Success;
        installingChecklistStep.State = ChecklistStepState.Running;
        StatusMessage = "Detecting installation state...";
        requestDetect();
    }

    [RelayCommand]
    private void RepairMaintenance()
    {
        if (!IsMaintenanceMode)
        {
            return;
        }

        SelectedMaintenanceAction = InstallerMaintenanceAction.Repair;
        ContinueMaintenance();
    }

    [RelayCommand]
    private void UninstallMaintenance()
    {
        if (!IsMaintenanceMode)
        {
            return;
        }

        SelectedMaintenanceAction = InstallerMaintenanceAction.Uninstall;
        ContinueMaintenance();
    }

    [RelayCommand]
    private void CloseInstaller()
    {
        if (Screen == InstallerScreen.Finished)
        {
            closeInstallerAction();
            return;
        }

        if (!ConfirmCancellation())
        {
            return;
        }

        if (installStarted)
        {
            HandleCancellation();
            return;
        }

        State = InstallerState.FinishedCancelled;
        Screen = InstallerScreen.Finished;
        StatusMessage = "Installation cancelled.";
        ReinstallCommand.Cancel();
    }

    private bool ConfirmCancellation()
    {
        try
        {
            return requestCancelConfirmation();
        }
        catch
        {
            return false;
        }
    }

    private static bool ShowCancelConfirmationMessage()
    {
        if (Application.Current is null)
        {
            return false;
        }

        var result = FluxoMessageBox.Show(
            Application.Current.MainWindow,
            "Are you sure you want to cancel the installation?",
            "Cancel installation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        return result == MessageBoxResult.Yes;
    }

    private static bool ShowUninstallConfirmationMessage()
    {
        if (Application.Current is null)
        {
            return false;
        }

        var result = FluxoMessageBox.Show(
            Application.Current.MainWindow,
            "Are you sure you want to uninstall fluxo?",
            "Confirm uninstallation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        return result == MessageBoxResult.Yes;
    }

    private static bool ShowTerminateRunningAppConfirmation(InstallerRequestedOperation operation)
    {
        if (Application.Current is null)
        {
            return false;
        }

        var operationText = operation switch
        {
            InstallerRequestedOperation.Repair => "repairing",
            InstallerRequestedOperation.Uninstall => "uninstalling",
            _ => "installing",
        };

        var result = FluxoMessageBox.Show(
            Application.Current.MainWindow,
            $"Fluxo is currently running. Do you want to close it now and continue {operationText}?",
            "Close Fluxo first?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        return result == MessageBoxResult.Yes;
    }

    private InstallerRequestedOperation GetStartupPreflightOperation()
    {
        if (IsUninstallMode)
        {
            return InstallerRequestedOperation.Uninstall;
        }

        if (IsMaintenanceMode)
        {
            return InstallerRequestedOperation.Repair;
        }

        return InstallerRequestedOperation.Install;
    }

    private void HandleCancellation()
    {
        SetRollbackOnlyChecklist();
        rollbackChecklistStep.State = ChecklistStepState.Running;
        StatusMessage = "Cancelling installation. Starting rollback...";
        runtimeInstaller.RequestCancellation();

        var rollbackFailures = ExecuteRollbackAndCleanup();
        if (rollbackFailures.Count == 0)
        {
            rollbackChecklistStep.State = ChecklistStepState.Success;
            StatusMessage = "Installation cancelled. Rollback completed.";
        }
        else
        {
            rollbackChecklistStep.State = ChecklistStepState.Failed;
            StatusMessage = $"Installation cancelled. {string.Join(" ", rollbackFailures)}";
        }

        State = InstallerState.FinishedCancelled;
        Screen = InstallerScreen.Finished;
    }

    private void EnsureDefaultChecklistSteps()
    {
        if (ChecklistSteps.Count == 3
            && ReferenceEquals(ChecklistSteps[0], prerequisitesChecklistStep)
            && ReferenceEquals(ChecklistSteps[1], installingChecklistStep)
            && ReferenceEquals(ChecklistSteps[2], cleanUpChecklistStep))
        {
            return;
        }

        ChecklistSteps.Clear();
        ChecklistSteps.Add(prerequisitesChecklistStep);
        ChecklistSteps.Add(installingChecklistStep);
        ChecklistSteps.Add(cleanUpChecklistStep);
    }

    private void SetRollbackOnlyChecklist()
    {
        if (ChecklistSteps.Count == 1 && ReferenceEquals(ChecklistSteps[0], rollbackChecklistStep))
        {
            return;
        }

        ChecklistSteps.Clear();
        ChecklistSteps.Add(rollbackChecklistStep);
    }

    private List<string> ExecuteRollbackAndCleanup()
    {
        var rollbackFailures = new List<string>();

        if (!isRollbackConfigured)
        {
            rollbackFailures.Add("Rollback unavailable: callback is not configured.");
        }
        else
        {
            try
            {
                if (!requestRollback())
                {
                    rollbackFailures.Add("Rollback failed.");
                }
            }
            catch (Exception ex)
            {
                rollbackFailures.Add($"Rollback failed: {ex.Message}");
            }
        }

        try
        {
            runtimeInstaller.RollbackRuntimeInstalledByFluxoAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            rollbackFailures.Add($"Runtime rollback failed: {ex.Message}");
        }

        if (!ShouldDeleteInstallFolder())
        {
            cleanUpChecklistStep.State = ChecklistStepState.Success;
            return rollbackFailures;
        }

        cleanUpChecklistStep.State = ChecklistStepState.Running;
        try
        {
            var installFolder = GetInstallFolderForCurrentRun();
            if (!IsSafeDeleteTarget(installFolder))
            {
                throw new InvalidOperationException("Cleanup rejected: install folder path is unsafe for recursive deletion.");
            }

            if (directoryExists(installFolder))
            {
                deleteDirectory(installFolder);
            }

            cleanUpChecklistStep.State = ChecklistStepState.Success;
        }
        catch (Exception ex)
        {
            cleanUpChecklistStep.State = ChecklistStepState.Failed;
            rollbackFailures.Add($"Cleanup failed: {ex.Message}");
        }

        return rollbackFailures;
    }

    public void SetCloseAction(Action? closeAction)
    {
        if (hasConstructorCloseInstallerAction)
        {
            return;
        }

        closeInstallerAction = closeAction ?? (() => { });
    }

    private static bool IsValidInstallFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (!Path.IsPathRooted(path))
        {
            return false;
        }

        return path.IndexOfAny(Path.GetInvalidPathChars()) < 0;
    }

    partial void OnInstallFolderChanged(string value)
    {
        InstallCommand.NotifyCanExecuteChanged();
    }

    partial void OnStateChanged(InstallerState value)
    {
        ReinstallCommand.NotifyCanExecuteChanged();
        DeclineReinstallCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanEditInstallFolder));
        OnPropertyChanged(nameof(IsInstallFolderReadOnly));
        ChangeDirectoryCommand.NotifyCanExecuteChanged();
        InstallCommand.NotifyCanExecuteChanged();
        LaunchAppCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(FinishedTitle));
        OnPropertyChanged(nameof(FinishedSubtitle));
    }

    partial void OnSelectedMaintenanceActionChanged(InstallerMaintenanceAction value)
    {
        OnPropertyChanged(nameof(IsRepairSelected));
        OnPropertyChanged(nameof(IsUninstallSelected));
    }

    private void ResetChecklistStates()
    {
        foreach (var checklistStep in ChecklistSteps)
        {
            checklistStep.State = ChecklistStepState.Pending;
        }
    }

    private void HandlePostStartFailure(string message)
    {
        if (installingChecklistStep.State == ChecklistStepState.Running
            || installingChecklistStep.State == ChecklistStepState.Pending)
        {
            installingChecklistStep.State = ChecklistStepState.Failed;
        }

        SetRollbackOnlyChecklist();
        rollbackChecklistStep.State = ChecklistStepState.Running;
        StatusMessage = $"{message} Starting rollback...";

        var rollbackFailures = ExecuteRollbackAndCleanup();

        if (rollbackFailures.Count == 0)
        {
            rollbackChecklistStep.State = ChecklistStepState.Success;
            State = InstallerState.FinishedFailed;
            Screen = InstallerScreen.Finished;
            StatusMessage = $"{message} Rollback completed.";
            return;
        }

        rollbackChecklistStep.State = ChecklistStepState.Failed;
        State = InstallerState.FinishedFailed;
        Screen = InstallerScreen.Finished;
        StatusMessage = $"{message} {string.Join(" ", rollbackFailures)}";
    }

    private bool ShouldDeleteInstallFolder()
    {
        return installStarted && !installFolderExistedBeforeInstall;
    }

    private string GetInstallFolderForCurrentRun()
    {
        return string.IsNullOrWhiteSpace(installFolderForCurrentRun) ? InstallFolder : installFolderForCurrentRun;
    }

    private static bool IsSafeDeleteTarget(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(path);
        var rootPath = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return false;
        }

        var normalizedFullPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRootPath = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !string.Equals(normalizedFullPath, normalizedRootPath, StringComparison.OrdinalIgnoreCase);
    }

    private static void LaunchInstalledApp(string executablePath)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory,
        };
        if (Process.Start(startInfo) is null)
        {
            throw new InvalidOperationException("Windows did not return a started Fluxo process.");
        }
    }

    private void StartUninstall()
    {
        RequestedOperation = InstallerRequestedOperation.Uninstall;
        installingChecklistStep.Label = InstallingChecklistLabel;
        if (!ConfirmUninstallRequest())
        {
            HandleUninstallCancelled();
            return;
        }

        if (!EnsureFluxoCanBeStoppedForOperation(RequestedOperation))
        {
            return;
        }

        Screen = InstallerScreen.Uninstall;
        State = InstallerState.Installing;
        installStarted = true;
        installFolderExistedBeforeInstall = true;
        installFolderForCurrentRun = ResolveInstallFolderForCurrentRun();
        EnsureDefaultChecklistSteps();
        ResetChecklistStates();
        prerequisitesChecklistStep.State = ChecklistStepState.Success;
        installingChecklistStep.State = ChecklistStepState.Running;
        StatusMessage = "Detecting installed version...";
        requestDetect();
    }

    private bool ConfirmUninstallRequest()
    {
        try
        {
            return requestUninstallConfirmation();
        }
        catch
        {
            return false;
        }
    }

    private void HandleUninstallCancelled()
    {
        installStarted = false;
        if (IsMaintenanceMode)
        {
            RequestedOperation = InstallerRequestedOperation.Install;
            Screen = InstallerScreen.AppFound;
            State = InstallerState.Welcome;
            StatusMessage = "Uninstallation cancelled.";
            return;
        }

        Screen = InstallerScreen.Finished;
        State = InstallerState.FinishedCancelled;
        StatusMessage = "Uninstallation cancelled.";
    }

    private bool EnsureFluxoCanBeStoppedForOperation(InstallerRequestedOperation operation)
    {
        IReadOnlyList<int> runningProcessIds;
        try
        {
            runningProcessIds = getRunningFluxoProcessIds();
        }
        catch
        {
            runningProcessIds = Array.Empty<int>();
        }

        if (runningProcessIds.Count == 0)
        {
            return true;
        }

        if (!requestTerminateRunningAppConfirmation(operation))
        {
            BlockOperationAndFinish(
                operation,
                "is still open",
                operation == InstallerRequestedOperation.Install
                    ? "Please close fluxo and run setup again."
                    : "Please close fluxo and run the repairer again.");
            return false;
        }

        foreach (var processId in runningProcessIds)
        {
            if (!tryTerminateProcessById(processId))
            {
                BlockOperationAndFinish(
                    operation,
                    "could not be terminated",
                    operation == InstallerRequestedOperation.Install
                        ? "Please close fluxo and run setup again."
                        : "Please close fluxo and run the repairer again.");
                return false;
            }
        }

        return true;
    }

    private void BlockOperationAndFinish(
        InstallerRequestedOperation operation,
        string reason,
        string retrySuffix)
    {
        installStarted = false;
        Screen = InstallerScreen.Finished;
        State = InstallerState.FinishedFailed;
        var operationLabel = operation switch
        {
            InstallerRequestedOperation.Repair => "Repair",
            InstallerRequestedOperation.Uninstall => "Uninstallation",
            _ => "Installation",
        };
        StatusMessage = $"{operationLabel} did not run because fluxo {reason}. {retrySuffix}";
    }

    private void CompleteUninstall()
    {
        prerequisitesChecklistStep.State = ChecklistStepState.Success;
        installingChecklistStep.State = ChecklistStepState.Success;
        cleanUpChecklistStep.State = ChecklistStepState.Running;
        if (!TryDeleteInstallFolderAfterUninstall(out var cleanupError)
            || !TryDeleteResidualStateAfterUninstall(out cleanupError))
        {
            cleanUpChecklistStep.State = ChecklistStepState.Failed;
            Screen = InstallerScreen.Finished;
            State = InstallerState.FinishedFailed;
            StatusMessage = $"Uninstallation failed: {cleanupError}";
            return;
        }

        var runtimeUninstallResult = runtimeInstaller.UninstallOwnedRuntimeAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        if (runtimeUninstallResult.Status == DotNetRuntimeUninstallStatus.Failed)
        {
            cleanUpChecklistStep.State = ChecklistStepState.Failed;
            Screen = InstallerScreen.Finished;
            State = InstallerState.FinishedFailed;
            StatusMessage = $"Uninstallation cleanup failed: {runtimeUninstallResult.Message}";
            return;
        }

        cleanUpChecklistStep.State = ChecklistStepState.Success;
        Screen = InstallerScreen.Finished;
        State = InstallerState.FinishedUninstalled;
        StatusMessage = "Uninstallation complete.";
    }

    private void StartMaintenance()
    {
        RequestedOperation = InstallerRequestedOperation.Install;
        SelectedMaintenanceAction = InstallerMaintenanceAction.Repair;
        installingChecklistStep.Label = InstallingChecklistLabel;
        Screen = InstallerScreen.AppFound;
        State = InstallerState.Welcome;
        installStarted = false;
        installFolderExistedBeforeInstall = true;
        installFolderForCurrentRun = ResolveInstallFolderForCurrentRun();
        StatusMessage = string.Empty;
    }

    private bool EnsureRepairerExecutable(out string errorMessage)
    {
        errorMessage = string.Empty;

        var sourcePath = bundleExecutablePath;
        if (string.IsNullOrWhiteSpace(sourcePath) || !fileExists(sourcePath))
        {
            errorMessage = "Installation failed: installer bundle executable was not found.";
            return false;
        }

        string fullSource;
        string fullDest;
        try
        {
            var destinationPath = Path.Combine(GetInstallFolderForCurrentRun(), RepairerExecutableName);
            fullSource = Path.GetFullPath(sourcePath);
            fullDest = Path.GetFullPath(destinationPath);
        }
        catch (Exception ex)
        {
            errorMessage = $"Installation failed: could not prepare repairer executable. {ex.Message}";
            return false;
        }

        if (string.Equals(fullSource, fullDest, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            if (fileExists(fullDest))
            {
                try
                {
                    var attributes = File.GetAttributes(fullDest);
                    if ((attributes & FileAttributes.ReadOnly) != 0)
                    {
                        File.SetAttributes(fullDest, attributes & ~FileAttributes.ReadOnly);
                    }
                }
                catch (IOException)
                {
                    // Stale existence checks or race with deletion; copy may still succeed.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            Exception? lastRetriableError = null;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    copyFile(fullSource, fullDest, true);
                    return true;
                }
                catch (Exception ex) when (attempt < 2 && IsRetriableRepairerCopyException(ex))
                {
                    lastRetriableError = ex;
                    Thread.Sleep(250 * (attempt + 1));
                }
                catch (Exception ex)
                {
                    errorMessage = $"Installation failed: could not prepare repairer executable. {ex.Message}";
                    return false;
                }
            }

            errorMessage =
                $"Installation failed: could not prepare repairer executable. {lastRetriableError?.Message ?? "The operation failed after multiple attempts."}";
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = $"Installation failed: could not prepare repairer executable. {ex.Message}";
            return false;
        }
    }

    private static bool IsRetriableRepairerCopyException(Exception ex) =>
        ex is IOException or UnauthorizedAccessException;

    private bool TryDeleteResidualStateAfterUninstall(out string cleanupError)
    {
        cleanupError = string.Empty;

        try
        {
            foreach (var registryView in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                deleteLocalMachineRegistrySubKeyTree(
                    registryView,
                    InstalledVersionRegistryReader.InstalledVersionSubKeyPath);
            }
        }
        catch (Exception ex)
        {
            cleanupError = $"could not delete installed version registry key. {ex.Message}";
            return false;
        }

        if (!directoryExists(localApplicationDataFluxoFolder))
        {
            return true;
        }

        if (!IsSafeDeleteTarget(localApplicationDataFluxoFolder))
        {
            cleanupError = "cleanup rejected because LocalAppData folder path is unsafe for recursive deletion.";
            return false;
        }

        try
        {
            deleteDirectory(localApplicationDataFluxoFolder);
            return true;
        }
        catch (Exception ex)
        {
            cleanupError = $"could not delete LocalAppData folder. {ex.Message}";
            return false;
        }
    }

    private bool TryDeleteInstallFolderAfterUninstall(out string cleanupError)
    {
        cleanupError = string.Empty;
        var installFolder = GetInstallFolderForCurrentRun();
        if (!IsSafeDeleteTarget(installFolder))
        {
            cleanupError = "cleanup rejected because install folder path is unsafe for recursive deletion.";
            return false;
        }

        if (!directoryExists(installFolder))
        {
            return true;
        }

        try
        {
            DeleteInstallFolderContentsExceptRepairer(installFolder);
            ScheduleDeferredInstallFolderCleanup(installFolder);
            return true;
        }
        catch (Exception ex)
        {
            cleanupError = ex.Message;
            return false;
        }
    }

    private void DeleteInstallFolderContentsExceptRepairer(string installFolder)
    {
        foreach (var entryPath in enumerateFileSystemEntries(installFolder))
        {
            var entryName = Path.GetFileName(entryPath);
            if (string.Equals(entryName, RepairerExecutableName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (directoryExists(entryPath))
            {
                deleteDirectory(entryPath);
                continue;
            }

            deleteFile(entryPath);
        }
    }

    private void ScheduleDeferredInstallFolderCleanup(string installFolder)
    {
        var repairerPath = Path.Combine(installFolder, RepairerExecutableName);
        var fluxoFolderPath = ResolveFluxoFolderCleanupTarget(installFolder);
        var scriptPath = createDeferredCleanupScriptPath();
        var scriptContent = BuildDeferredCleanupScript(installFolder, repairerPath, fluxoFolderPath);
        writeAllText(scriptPath, scriptContent);

        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/d /c \"\"{scriptPath}\"\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetTempPath(),
        };

        startProcess(startInfo);
    }

    private static string ResolveFluxoFolderCleanupTarget(string installFolder)
    {
        var normalizedInstallFolder = Path.GetFullPath(installFolder)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(
                Path.GetFileName(normalizedInstallFolder),
                FluxoFolderName,
                StringComparison.OrdinalIgnoreCase))
        {
            return normalizedInstallFolder;
        }

        var parentDirectory = Path.GetDirectoryName(normalizedInstallFolder);
        if (!string.IsNullOrWhiteSpace(parentDirectory)
            && string.Equals(
                Path.GetFileName(parentDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                FluxoFolderName,
                StringComparison.OrdinalIgnoreCase))
        {
            return parentDirectory;
        }

        return normalizedInstallFolder;
    }

    private static void DeleteLocalMachineRegistrySubKeyTree(RegistryView registryView, string subKeyPath)
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView);
        baseKey.DeleteSubKeyTree(subKeyPath, throwOnMissingSubKey: false);
    }

    private static string BuildDeferredCleanupScript(string installFolder, string repairerPath, string fluxoFolderPath)
    {
        static string QuoteForBatchValue(string value) => value.Replace("\"", "\"\"");

        var builder = new StringBuilder();
        builder.AppendLine("@echo off");
        builder.AppendLine("setlocal");
        builder.AppendLine("cd /d \"%TEMP%\" >nul 2>nul");
        builder.AppendLine($"set \"INSTALL_DIR={QuoteForBatchValue(installFolder)}\"");
        builder.AppendLine($"set \"REPAIRER_PATH={QuoteForBatchValue(repairerPath)}\"");
        builder.AppendLine($"set \"FLUXO_DIR={QuoteForBatchValue(fluxoFolderPath)}\"");
        builder.AppendLine($"set /a \"RETRIES={DeferredCleanupRetryCount}\"");
        builder.AppendLine(":wait_loop");
        builder.AppendLine("if not exist \"%REPAIRER_PATH%\" goto delete_folder");
        builder.AppendLine("del /f /q \"%REPAIRER_PATH%\" >nul 2>nul");
        builder.AppendLine("if not exist \"%REPAIRER_PATH%\" goto delete_folder");
        builder.AppendLine("set /a RETRIES-=1");
        builder.AppendLine("if %RETRIES% LEQ 0 goto delete_folder");
        builder.AppendLine($"timeout /t {DeferredCleanupRetryDelaySeconds} /nobreak >nul");
        builder.AppendLine("goto wait_loop");
        builder.AppendLine(":delete_folder");
        builder.AppendLine($"set /a \"RETRIES={DeferredCleanupRetryCount}\"");
        builder.AppendLine(":folder_loop");
        builder.AppendLine("rmdir /s /q \"%INSTALL_DIR%\" >nul 2>nul");
        builder.AppendLine("if not exist \"%INSTALL_DIR%\" goto delete_fluxo_folder");
        builder.AppendLine("set /a RETRIES-=1");
        builder.AppendLine("if %RETRIES% LEQ 0 goto delete_fluxo_folder");
        builder.AppendLine($"timeout /t {DeferredCleanupRetryDelaySeconds} /nobreak >nul");
        builder.AppendLine("goto folder_loop");
        builder.AppendLine(":delete_fluxo_folder");
        builder.AppendLine("if /I \"%FLUXO_DIR%\"==\"%INSTALL_DIR%\" goto cleanup_self");
        builder.AppendLine($"set /a \"RETRIES={DeferredCleanupRetryCount}\"");
        builder.AppendLine(":fluxo_folder_loop");
        builder.AppendLine("rmdir /s /q \"%FLUXO_DIR%\" >nul 2>nul");
        builder.AppendLine("if not exist \"%FLUXO_DIR%\" goto cleanup_self");
        builder.AppendLine("set /a RETRIES-=1");
        builder.AppendLine("if %RETRIES% LEQ 0 goto cleanup_self");
        builder.AppendLine($"timeout /t {DeferredCleanupRetryDelaySeconds} /nobreak >nul");
        builder.AppendLine("goto fluxo_folder_loop");
        builder.AppendLine(":cleanup_self");
        builder.AppendLine("del /f /q \"%~f0\" >nul 2>nul");
        builder.AppendLine("exit /b 0");
        return builder.ToString();
    }

    private string ResolveInstallFolderForCurrentRun()
    {
        if (!string.IsNullOrWhiteSpace(installFolderForCurrentRun)
            && IsValidInstallFolder(installFolderForCurrentRun))
        {
            return installFolderForCurrentRun;
        }

        if (!TryResolveInstallFolderFromRepairerPath(out var resolvedFromRepairer))
        {
            return InstallFolder;
        }

        if (IsValidInstallFolder(resolvedFromRepairer))
        {
            InstallFolder = resolvedFromRepairer;
            return resolvedFromRepairer;
        }

        return InstallFolder;
    }

    private bool TryResolveInstallFolderFromRepairerPath(out string resolvedPath)
    {
        resolvedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(bundleExecutablePath))
        {
            return false;
        }

        var executableName = Path.GetFileName(bundleExecutablePath);
        if (!string.Equals(executableName, RepairerExecutableName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parentDirectory = Path.GetDirectoryName(bundleExecutablePath);
        if (string.IsNullOrWhiteSpace(parentDirectory))
        {
            return false;
        }

        resolvedPath = parentDirectory;
        return true;
    }

    private static IReadOnlyList<int> GetRunningFluxoProcessIds()
    {
        var processIds = new List<int>();
        foreach (var process in Process.GetProcessesByName("fluxo"))
        {
            try
            {
                processIds.Add(process.Id);
            }
            finally
            {
                process.Dispose();
            }
        }

        return processIds;
    }

    private static bool TryTerminateProcessById(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return true;
            }

            process.Kill(entireProcessTree: true);
            return process.WaitForExit(ProcessTerminationTimeoutMilliseconds);
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }
}
