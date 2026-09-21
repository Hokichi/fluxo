using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Threading;
using System.Windows.Interop;
using System.Windows.Threading;
using Fluxo.Installer.Models;
using Fluxo.Installer.Services;
using Fluxo.Installer.ViewModels;
using WixToolset.BootstrapperApplicationApi;

namespace Fluxo.Installer;

internal sealed class InstallerBootstrapperApplication : BootstrapperApplication
{
    private const int SuccessExitCode = 0;
    private const int CancelExitCode = 1602;
    private const int FailureExitCode = 1;
    private const string InstalledExecutableName = "fluxo.exe";
    private const string DefaultInstallFolderName = "fluxo";
    private const string InstallFolderArgument = "--install-folder";

    private static readonly string DiagnosticLogPath = Path.Combine(
        Path.GetTempPath(),
        "Fluxo.Installer",
        "bootstrapper-error.log");

    private IBootstrapperCommand? _command;
    private InstallerViewModel? _viewModel;
    private Dispatcher? _uiDispatcher;
    private volatile bool _lastApplyFailed;
    private volatile bool _headlessMode;
    private volatile int _headlessExitCode = FailureExitCode;
    private readonly ManualResetEventSlim _headlessCompleted = new(false);
    private string? _currentBundleVersion;
    private string? _highestDetectedInstalledVersion;
    private bool _hasOlderRelatedBundle;
    private string? _registryInstalledVersion;
    private string? _registryInstallLocation;
    private string? _installedExecutableVersion;
    private readonly Action releaseSingleInstanceMutex;

    public InstallerBootstrapperApplication(Action? releaseSingleInstanceMutex = null)
    {
        this.releaseSingleInstanceMutex = releaseSingleInstanceMutex ?? (() => { });
        DetectBegin += OnDetectBegin;
        DetectRelatedBundle += OnDetectRelatedBundle;
        DetectComplete += OnDetectComplete;
        PlanComplete += OnPlanComplete;
        ApplyComplete += OnApplyComplete;
    }

    protected override void Run()
    {
        if (TryExitForInteractiveElevationRelaunch())
        {
            engine.Quit(SuccessExitCode);
            return;
        }

        var exitCode = ShouldRunInteractiveUi()
            ? RunUiWithStaGuard()
            : RunHeadless();

        engine.Quit(exitCode);
    }

    /// <summary>
    /// Burn often applies the per-machine MSI elevated while the managed bootstrapper host stays
    /// medium-integrity. Post-apply steps (copy repairer, rollback folder cleanup) then hit
    /// "Access denied" under Program Files. Relaunch interactively with <c>runas</c> first.
    /// </summary>
    private bool TryExitForInteractiveElevationRelaunch()
    {
        if (!ShouldRunInteractiveUi() || IsProcessElevated())
        {
            return false;
        }

        var bundlePath = ResolveBundlePathForElevationRelaunch();
        if (!InstallerElevationRelaunch.ShouldRelaunchForElevation(
                isInteractive: true,
                isElevated: false,
                bundlePath))
        {
            return false;
        }

        try
        {
            releaseSingleInstanceMutex();
            _ = Process.Start(InstallerElevationRelaunch.CreateStartInfo(
                bundlePath!,
                BuildElevationRelaunchArguments()));
            return true;
        }
        catch (Win32Exception)
        {
            // User cancelled UAC or elevation launch failed; continue without elevation.
            return false;
        }
    }

    private static bool IsProcessElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private string? ResolveBundlePathForElevationRelaunch()
    {
        var sourceProcessPath = TryGetEngineVariable("WixBundleSourceProcessPath");
        var originalSource = TryGetEngineVariable("WixBundleOriginalSource");
        var processPath = TryGetProcessPath();
        var preferredPath = InstallerElevationRelaunch.SelectBundlePathForElevationRelaunch(
            sourceProcessPath,
            originalSource,
            processPath);

        string?[] candidates =
        [
            preferredPath,
            originalSource,
            sourceProcessPath,
            processPath,
        ];

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? TryGetProcessPath()
    {
        try
        {
            return Environment.ProcessPath;
        }
        catch
        {
            return null;
        }
    }

    private string? TryGetEngineVariable(string name)
    {
        try
        {
            var value = engine.GetVariableString(name);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    protected override void OnCreate(CreateEventArgs args)
    {
        base.OnCreate(args);
        _command = args.Command;
    }

    private int RunUiWithStaGuard()
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return RunUi();
        }

        var exitCode = FailureExitCode;
        var uiThread = new Thread(() => exitCode = RunUi())
        {
            IsBackground = false,
        };
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();
        uiThread.Join();
        return exitCode;
    }

    private int RunHeadless()
    {
        _headlessMode = true;
        _headlessExitCode = FailureExitCode;
        _headlessCompleted.Reset();
        _lastApplyFailed = false;

        try
        {
            engine.Detect();
            _headlessCompleted.Wait();
            return _headlessExitCode;
        }
        catch (Exception ex)
        {
            LogFailure(ex);
            return FailureExitCode;
        }
        finally
        {
            _headlessMode = false;
            _headlessCompleted.Reset();
        }
    }

    private bool ShouldRunInteractiveUi()
    {
        // Burn invokes related bundles in quiet/embedded modes during upgrade cleanup.
        // Those runs should stay headless to avoid spawning visible duplicate installer windows.
        return _command is null || _command.Display == Display.Full;
    }

    private int RunUi()
    {
        try
        {
#if DEBUG
            // Pauses execution until you attach a debugger.
            // A dialog will appear showing the process ID.
            System.Diagnostics.Debugger.Launch();
#endif
            var app = new App();
            app.InitializeComponent();
            _uiDispatcher = app.Dispatcher;

            var window = new Views.MainWindow();

            _viewModel = new InstallerViewModel(
                setInstallFolderVariable: value => engine.SetVariableString("InstallFolder", value, formatted: false),
                requestDetect: () =>
                {
                    _lastApplyFailed = false;
                    engine.Detect();
                },
                requestPlan: () => engine.Plan(GetRequestedLaunchAction(), GetRequestedBundleScope()),
                requestApply: () =>
                {
                    try
                    {
                        var parentHandle = new WindowInteropHelper(window).EnsureHandle();
                        engine.Apply(parentHandle);
                    }
                    catch (Exception ex)
                    {
                        LogFailure(ex);
                        _lastApplyFailed = true;
                        DispatchToUi(() => _viewModel?.OnApplyComplete(FailureExitCode));
                    }
                },
                // Burn performs rollback internally before ApplyComplete on apply failures.
                // We report rollback as successful only when the most recent apply failed.
                requestRollback: () => _lastApplyFailed,
                operationMode: GetOperationMode(),
                bundleExecutablePath: GetBundleExecutablePathForViewModel(),
                requestedInstallFolder: GetRequestedInstallFolder(),
                closeInstallerAction: () =>
                {
                    if (window.Dispatcher.CheckAccess())
                    {
                        window.Close();
                        return;
                    }

                    window.Dispatcher.Invoke(window.Close);
                });

            window.DataContext = _viewModel;
            _viewModel.Begin();

            _ = app.Run(window);
            return _viewModel.ExitCode;
        }
        catch (OperationCanceledException)
        {
            return CancelExitCode;
        }
        catch (Exception ex)
        {
            LogFailure(ex);
            return FailureExitCode;
        }
    }

    private static void LogFailure(Exception exception)
    {
        try
        {
            var directory = Path.GetDirectoryName(DiagnosticLogPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var content = $"{DateTimeOffset.UtcNow:O}{Environment.NewLine}{exception}{Environment.NewLine}";
            File.AppendAllText(DiagnosticLogPath, content);
        }
        catch
        {
        }
    }

    private void OnDetectBegin(object? sender, DetectBeginEventArgs e)
    {
        _highestDetectedInstalledVersion = null;
        _currentBundleVersion = GetCurrentBundleVersion();
        _hasOlderRelatedBundle = false;
        _registryInstalledVersion = InstalledVersionRegistryReader.ReadInstalledVersion();
        _registryInstallLocation = InstallerInstallLocationResolver.Resolve(
            InstalledVersionRegistryReader.ReadInstallLocations(),
            _viewModel?.InstallFolder ?? GetRequestedInstallFolder(),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), DefaultInstallFolderName),
            Directory.Exists);
        _installedExecutableVersion = GetInstalledExecutableVersion();
    }

    private void OnDetectRelatedBundle(object? sender, DetectRelatedBundleEventArgs e)
    {
        if (!IsInstalledRelatedBundle(e.RelationType) || string.IsNullOrWhiteSpace(e.Version))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_currentBundleVersion)
            && TryCompareVersions(e.Version, _currentBundleVersion, out var comparisonToCurrent)
            && comparisonToCurrent < 0)
        {
            _hasOlderRelatedBundle = true;
        }

        if (string.IsNullOrWhiteSpace(_highestDetectedInstalledVersion))
        {
            _highestDetectedInstalledVersion = e.Version;
            return;
        }

        if (TryCompareVersions(e.Version, _highestDetectedInstalledVersion, out var comparison) && comparison > 0)
        {
            _highestDetectedInstalledVersion = e.Version;
        }
    }

    private void OnDetectComplete(object? sender, DetectCompleteEventArgs e)
    {
        var operationMode = GetOperationMode();
        var upToDateDecision = InstallerUpToDateDecision.Evaluate(
            operationMode,
            e.Status,
            _currentBundleVersion,
            _highestDetectedInstalledVersion,
            _registryInstalledVersion,
            _installedExecutableVersion,
            (left, right) => engine.CompareVersions(left, right),
            _hasOlderRelatedBundle);

        if (_headlessMode)
        {
            if (e.Status != 0)
            {
                _headlessExitCode = e.Status;
                _headlessCompleted.Set();
                return;
            }
            if (upToDateDecision.ShouldSkipInstall)
            {
                _headlessExitCode = SuccessExitCode;
                _headlessCompleted.Set();
                return;
            }

            engine.Plan(GetRequestedLaunchAction(), GetRequestedBundleScope());

            return;
        }

        DispatchToUi(() => _viewModel?.OnInstallationDetected(
            e.Status, upToDateDecision, _registryInstallLocation!));
    }

    private void OnPlanComplete(object? sender, PlanCompleteEventArgs e)
    {
        if (_headlessMode)
        {
            if (e.Status != 0)
            {
                _headlessExitCode = e.Status;
                _headlessCompleted.Set();
                return;
            }

            engine.Apply(IntPtr.Zero);

            return;
        }

        DispatchToUi(() => _viewModel?.OnPlanComplete(e.Status));
    }

    private void OnApplyComplete(object? sender, ApplyCompleteEventArgs e)
    {
        _lastApplyFailed = e.Status != 0;

        if (_headlessMode)
        {
            _headlessExitCode = e.Status;
            _headlessCompleted.Set();
            return;
        }

        DispatchToUi(() => _viewModel?.OnApplyComplete(e.Status));
    }

    private void DispatchToUi(Action callback)
    {
        if (_uiDispatcher is null || _uiDispatcher.CheckAccess())
        {
            callback();
            return;
        }

        _uiDispatcher.Invoke(callback);
    }

    private string? GetCurrentBundleVersion()
    {
        try
        {
            var version = engine.GetVariableVersion("WixBundleVersion");
            if (!string.IsNullOrWhiteSpace(version))
            {
                return version;
            }

            version = engine.GetVariableString("WixBundleVersion");
            return string.IsNullOrWhiteSpace(version) ? null : version;
        }
        catch
        {
            return null;
        }
    }

    private string? GetInstalledExecutableVersion()
    {
        try
        {
            var installFolder = _registryInstallLocation ?? GetInstallFolderVariable();
            if (string.IsNullOrWhiteSpace(installFolder))
            {
                installFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    DefaultInstallFolderName);
            }

            var executablePath = Path.Combine(installFolder, InstalledExecutableName);
            if (!File.Exists(executablePath))
            {
                return null;
            }

            var versionInfo = FileVersionInfo.GetVersionInfo(executablePath);
            if (!string.IsNullOrWhiteSpace(versionInfo.FileVersion))
            {
                return versionInfo.FileVersion;
            }

            return string.IsNullOrWhiteSpace(versionInfo.ProductVersion)
                ? null
                : versionInfo.ProductVersion;
        }
        catch
        {
            return null;
        }
    }

    private string? GetInstallFolderVariable()
    {
        try
        {
            var installFolder = engine.GetVariableString("InstallFolder");
            return string.IsNullOrWhiteSpace(installFolder) ? null : installFolder;
        }
        catch
        {
            return null;
        }
    }

    private string? GetRequestedInstallFolder()
    {
        var commandLine = _command?.CommandLine;
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return null;
        }

        var args = SplitCommandLine(commandLine);
        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index];
            if (string.Equals(arg, InstallFolderArgument, StringComparison.OrdinalIgnoreCase))
            {
                return index + 1 < args.Count ? args[index + 1] : null;
            }

            var prefix = $"{InstallFolderArgument}=";
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return arg[prefix.Length..];
            }
        }

        return null;
    }

    private string BuildElevationRelaunchArguments()
    {
        var requestedInstallFolder = GetRequestedInstallFolder();
        if (string.IsNullOrWhiteSpace(requestedInstallFolder))
        {
            return string.Empty;
        }

        return $"{InstallFolderArgument} {QuoteCommandLineArgument(requestedInstallFolder)}";
    }

    private static string QuoteCommandLineArgument(string value) =>
        $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static IReadOnlyList<string> SplitCommandLine(string commandLine)
    {
        var args = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var character in commandLine)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                AddCurrent();
                continue;
            }

            current.Append(character);
        }

        AddCurrent();
        return args;

        void AddCurrent()
        {
            if (current.Length == 0)
            {
                return;
            }

            args.Add(current.ToString());
            current.Clear();
        }
    }

    private bool TryCompareVersions(string left, string right, out int comparison)
    {
        try
        {
            comparison = engine.CompareVersions(left, right);
            return true;
        }
        catch
        {
            comparison = 0;
            return false;
        }
    }

    private static bool IsInstalledRelatedBundle(RelationType relationType) =>
        relationType == RelationType.Detect
        || relationType == RelationType.Upgrade
        || relationType == RelationType.Update;

    private LaunchAction GetRequestedLaunchAction()
    {
        return InstallerLaunchActionResolver.Resolve(GetRequestedOperation());
    }

    private BundleScope GetRequestedBundleScope()
    {
        return _command?.Scope ?? BundleScope.Default;
    }

    private InstallerOperationMode GetOperationMode()
    {
        return InstallerOperationModeDetector.Detect(
            GetBundleOriginalSourcePath(),
            GetBundleSourceProcessPath(),
            GetCurrentExecutablePath());
    }

    private InstallerRequestedOperation GetRequestedOperation()
    {
        if (_viewModel is not null)
        {
            return _viewModel.RequestedOperation;
        }

        return GetOperationMode() == InstallerOperationMode.Uninstall
            ? InstallerRequestedOperation.Uninstall
            : InstallerRequestedOperation.Install;
    }

    private string GetBundleOriginalSourcePath()
    {
        try
        {
            var sourcePath = engine.GetVariableString("WixBundleOriginalSource");
            if (!string.IsNullOrWhiteSpace(sourcePath))
            {
                return sourcePath;
            }
        }
        catch
        {
        }

        return GetCurrentExecutablePath() ?? string.Empty;
    }

    private string GetBundleSourceProcessPath()
    {
        try
        {
            var sourcePath = engine.GetVariableString("WixBundleSourceProcessPath");
            if (!string.IsNullOrWhiteSpace(sourcePath))
            {
                return sourcePath;
            }
        }
        catch
        {
        }

        return GetBundleOriginalSourcePath();
    }

    private string GetBundleExecutablePathForViewModel()
    {
        return InstallerOperationModeDetector.SelectBundleExecutablePathForViewModel(
            TryGetEngineVariable("WixBundleSourceProcessPath"),
            TryGetEngineVariable("WixBundleOriginalSource"),
            GetBundleSourceProcessPath());
    }

    private static string? GetCurrentExecutablePath()
    {
        try
        {
            return Environment.ProcessPath;
        }
        catch
        {
            return null;
        }
    }
}
