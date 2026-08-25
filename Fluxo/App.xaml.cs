using Fluxo.Core.Constants;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Operations;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Core.Interfaces.Caching;
using Fluxo.Data.Context;
using Fluxo.Data.Extensions;
using Fluxo.Extensions;
using Fluxo.Services.Logging;
using Fluxo.Services.Notifications;
using Fluxo.Services.Dialogs;
using Fluxo.Services.Theming;
using Fluxo.Services.Ui;
using Fluxo.Services.Updates;
using Fluxo.Infrastructure.SingleInstance;
using Fluxo.ViewModels.Shell;
using Fluxo.Resources.Theming;
using Fluxo.Views.Shell;
using Fluxo.Views.Shell.Main;
using Fluxo.Views.Shell.Tray;
using Fluxo.Views.Shell.Wizard;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using MainVM = Fluxo.ViewModels.Shell.Main.MainVM;

namespace Fluxo;

/// <summary>
///     Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private const string StartupTrayArgument = "--startup-tray";
    private const string RestartTrayArgument = "--startup--tray";

    private enum StartupStageState
    {
        Started,
        Completed,
        Failed
    }

    private readonly IDataOperationRunner _dataOperationRunner;
    private readonly IBudgetAllocationPeriodSyncService _budgetAllocationPeriodSyncService;
    private readonly ITransactionService _transactionService;
    private readonly IAppDataService _appDataService;
    private readonly IAppDataCache _appDataCache;
    private readonly ThemeService _themeService;
    private readonly MainVM _mainVM;
    private readonly IStartupRegistrationService _startupRegistrationService;
    private readonly IUiSettleAwaiter _uiSettleAwaiter;
    private readonly IServiceProvider _serviceProvider;
    private readonly DispatcherTimer _trayLeftClickTimer;
    private Forms.NotifyIcon? _trayIcon;
    private TrayMenuPopup? _trayMenuPopup;
    private bool _isTrayLeftClickPending;
    private bool _isForcedShutdownRequested;
    private bool _isPrimaryActivationPending;
    private ISingleInstanceCoordinator? _singleInstanceCoordinator;

    public App()
    {
        InitializeBootstrapLogging();
        RegisterGlobalExceptionHandlers();

        var services = new ServiceCollection();
        services
            .AddFluxoData()
            .AddFluxoPresentation()
            .AddUIData();

        _serviceProvider = services.BuildServiceProvider();

        _mainVM = _serviceProvider.GetRequiredService<MainVM>();
        _dataOperationRunner = _serviceProvider.GetRequiredService<IDataOperationRunner>();
        _budgetAllocationPeriodSyncService = _serviceProvider.GetRequiredService<IBudgetAllocationPeriodSyncService>();
        _transactionService = _serviceProvider.GetRequiredService<ITransactionService>();
        _appDataService = _serviceProvider.GetRequiredService<IAppDataService>();
        _appDataCache = _serviceProvider.GetRequiredService<IAppDataCache>();
        _themeService = _serviceProvider.GetRequiredService<ThemeService>();
        _startupRegistrationService = _serviceProvider.GetRequiredService<IStartupRegistrationService>();
        _uiSettleAwaiter = _serviceProvider.GetRequiredService<IUiSettleAwaiter>();

        _trayLeftClickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
        _trayLeftClickTimer.Tick += OnTrayLeftClickTimerTick;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        LogStartupStage("single-instance check", StartupStageState.Started);
        _singleInstanceCoordinator ??= new SingleInstanceCoordinator();
        var shouldContinueStartup = SingleInstanceStartupPolicy.ShouldContinueStartup(
            _singleInstanceCoordinator,
            OnPrimaryActivationRequested);
        LogStartupStage("single-instance check", StartupStageState.Completed);

        if (!shouldContinueStartup)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            LogStartupStage("database directory", StartupStageState.Started);
            FluxoDbContextFactory.EnsureDatabaseDirectoryExists();
            LogStartupStage("database directory", StartupStageState.Completed);
            LogStartupStage("database backup", StartupStageState.Started);
            await BackupDatabaseOnStartupAsync();
            LogStartupStage("database backup", StartupStageState.Completed);
            LogStartupStage("database migration", StartupStageState.Started);
            await MigrateDatabaseAsync(_dataOperationRunner);
            LogStartupStage("database migration", StartupStageState.Completed);
            LogStartupStage("budget allocation period sync", StartupStageState.Started);
            await SyncBudgetAllocationPeriodAsync();
            LogStartupStage("budget allocation period sync", StartupStageState.Completed);
            LogStartupStage("username logging initialization", StartupStageState.Started);
            await InitializeLoggingAsync();
            LogStartupStage("username logging initialization", StartupStageState.Completed);

            LogStartupStage("startup loader", StartupStageState.Started);
            var loaderPopup = new StartupLoaderPopup();
            bool isFirstRun;

            try
            {
                loaderPopup.Show();
                await _uiSettleAwaiter.WaitForUiReadyAsync(loaderPopup);
                LogStartupStage("first-run setting", StartupStageState.Started);
                isFirstRun = await EnsureFirstRunSettingAsync(_dataOperationRunner);
                LogStartupStage("first-run setting", StartupStageState.Completed);
                await _uiSettleAwaiter.WaitForUiReadyAsync(loaderPopup);
                await InitializeApplicationDataAsync(
                    isFirstRun,
                    _appDataCache,
                    async () =>
                    {
                        LogStartupStage("post-termination cleanup", StartupStageState.Started);
                        await _transactionService.PostTerminationCleanupAsync();
                        LogStartupStage("post-termination cleanup", StartupStageState.Completed);
                    },
                    async () =>
                    {
                        LogStartupStage("startup registration sync", StartupStageState.Started);
                        await SyncRunAtStartupRegistrationAsync();
                        LogStartupStage("startup registration sync", StartupStageState.Completed);
                        LogStartupStage("startup update check", StartupStageState.Started);
                        // Floating update card is checked after MainWindow becomes visible.
                        LogStartupStage("startup update check", StartupStageState.Completed);
                    },
                    async () =>
                    {
                        LogStartupStage("main view model initialization", StartupStageState.Started);
                        await _mainVM.InitializeWithStartupStagesAsync(() =>
                            _uiSettleAwaiter.WaitForUiReadyAsync(loaderPopup));
                        LogStartupStage("main view model initialization", StartupStageState.Completed);
                    },
                    () => _uiSettleAwaiter.WaitForUiReadyAsync(loaderPopup));
                await RestoreThemeAsync();
                await _uiSettleAwaiter.WaitForUiReadyAsync(loaderPopup);
            }
            finally
            {
                loaderPopup.CloseLoader();
                LogStartupStage("startup loader", StartupStageState.Completed);
            }

            if (isFirstRun)
            {
                LogStartupStage("startup wizard", StartupStageState.Started);
                using var wizardScope = _serviceProvider!.CreateScope();
                var wizard = wizardScope.ServiceProvider.GetRequiredService<QuickSetupWizard>();
                wizard.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                wizard.ShowDialog();
                LogStartupStage("startup wizard", StartupStageState.Completed);
            }

            LogStartupStage("main window", StartupStageState.Started);
            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            EnsureTrayIconInitialized();
            var closeBehavior = await GetCloseBehaviorAsync();
            var shouldForegroundOnStartup = _isPrimaryActivationPending;
            var shouldHideToTrayOnStartup = ShouldHideMainWindowAtStartup(
                e.Args,
                closeBehavior,
                shouldForegroundOnStartup);

            if (shouldForegroundOnStartup)
            {
                _isPrimaryActivationPending = false;
                RestoreMainWindowFromTray();
            }
            else if (shouldHideToTrayOnStartup)
                HideMainWindowToTray(mainWindow);
            else
                mainWindow.Show();

            LogStartupStage("main window", StartupStageState.Completed);
        }
        catch (Exception exception)
        {
            LogStartupStage("startup failure", StartupStageState.Failed);
            FluxoLogManager.LogError(exception, "Unable to start Fluxo.");

            if (_serviceProvider?.GetService<IDialogService>() is { } dialogService)
                dialogService.ShowError(FluxoLogManager.CreateFailureMessage("start Fluxo"), "Fluxo");
            else
                FluxoMessageBox.Show(null, FluxoLogManager.CreateFailureMessage("start Fluxo"), "Fluxo",
                    MessageBoxButton.OK, MessageBoxImage.Error);

            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            DisposeTrayResources();
            _singleInstanceCoordinator?.Dispose();
            _singleInstanceCoordinator = null;
        }
        catch (Exception exception)
        {
            FluxoLogManager.LogFailureForProcess(exception, "dispose app resources during app shutdown");
        }

        FluxoLogManager.CloseAndFlush();
        base.OnExit(e);
    }

    private void OnPrimaryActivationRequested()
    {
        RunOnUiThread(() =>
        {
            if (MainWindow is not Fluxo.Views.Shell.Main.MainWindow)
            {
                _isPrimaryActivationPending = true;
                return;
            }

            _isPrimaryActivationPending = false;
            RestoreMainWindowFromTray();
        });
    }

    public async Task RunSetupWizardAsync()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        MainWindow?.Hide();

        try
        {
            using (var wizardScope = _serviceProvider!.CreateScope())
            {
                var wizard = wizardScope.ServiceProvider.GetRequiredService<QuickSetupWizard>();
                wizard.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                wizard.ShowDialog();
            }

            await _mainVM.Initialize();
        }
        catch (Exception exception)
        {
            FluxoLogManager.LogFailureForProcess(exception, "run setup wizard");
            throw;
        }
        finally
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            MainWindow?.Show();
        }
    }

    public void LaunchUpdateInstallerAndShutdown(string installerPath)
    {
        var installFolder = ResolveCurrentInstallFolder();
        var startInfo = AppUpdateInstallerLauncher.CreateStartInfo(installerPath, installFolder);
        var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException("The update installer did not start.");
        }

        _isForcedShutdownRequested = true;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Shutdown(0);
    }

    internal async Task<bool> TryHandleMainWindowClosingToTrayAsync(MainWindow mainWindow)
    {
        if (_isForcedShutdownRequested)
            return false;

        if (!await IsCloseBehaviorMinimizeToTrayAsync())
            return false;

        if (!TryCloseOwnedWindows(mainWindow))
            return true;

        EnsureTrayIconInitialized();
        HideMainWindowToTray(mainWindow);
        return true;
    }

    internal static bool TryCloseOwnedWindows(Window ownerWindow)
    {
        var ownedWindows = ownerWindow.OwnedWindows
            .Cast<Window>()
            .Where(window => window.IsVisible)
            .ToList();

        foreach (var ownedWindow in ownedWindows)
        {
            if (!TryCloseOwnedWindows(ownedWindow))
                return false;

            ownedWindow.Close();
            if (ownedWindow.IsVisible)
                return false;
        }

        return true;
    }

    private async Task<bool> IsCloseBehaviorMinimizeToTrayAsync()
    {
        return await GetCloseBehaviorAsync() == AppCloseBehavior.MinimizeToTray;
    }

    internal static async Task InitializeApplicationDataAsync(
        bool isFirstRun,
        IAppDataCache cache,
        Func<Task> postTerminationCleanupAsync,
        Func<Task> syncStartupRegistrationAsync,
        Func<Task> initializeMainAsync,
        Func<Task> betweenStagesAsync)
    {
        await postTerminationCleanupAsync();
        await betweenStagesAsync();
        await syncStartupRegistrationAsync();
        await betweenStagesAsync();
        LogStartupStage("application data cache", StartupStageState.Started);
        await cache.InitializeAsync();
        LogStartupStage("application data cache", StartupStageState.Completed);
        await betweenStagesAsync();
        if (!isFirstRun)
            await initializeMainAsync();
    }

    private async Task<AppCloseBehavior> GetCloseBehaviorAsync()
    {
        try
        {
            var setting = await _appDataService.GetUserSettingByNameAsync(UserSettingNames.CloseBehavior);
            return UserSettingValueParser.ParseCloseBehavior(setting?.Value, AppCloseBehavior.Exit);
        }
        catch (Exception exception)
        {
            FluxoLogManager.LogWarning(
                exception,
                "Unable to resolve close behavior from user settings. Falling back to default exit behavior.");
            return AppCloseBehavior.Exit;
        }
    }

    private async Task RestoreThemeAsync()
    {
        LogStartupStage("theme restoration", StartupStageState.Started);

        try
        {
            await _themeService.RestoreAsync();
            LogStartupStage("theme restoration", StartupStageState.Completed);
        }
        catch (Exception exception)
        {
            ThemeManager.SwitchTheme(ApplicationTheme.Dark);
            LogStartupStage("theme restoration", StartupStageState.Failed);
            FluxoLogManager.LogWarning(
                exception,
                "Unable to restore the saved theme. Falling back to dark mode.");
        }
    }

    private static string ResolveCurrentInstallFolder()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            var directory = Path.GetDirectoryName(processPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                return directory;
            }
        }

        return AppContext.BaseDirectory;
    }

    internal static bool ShouldHideMainWindowAtStartup(
        string[] args,
        AppCloseBehavior closeBehavior,
        bool isPrimaryActivationPending)
    {
        return !isPrimaryActivationPending &&
               args.Any(arg => string.Equals(arg, StartupTrayArgument, StringComparison.OrdinalIgnoreCase)) &&
               closeBehavior == AppCloseBehavior.MinimizeToTray;
    }

    private async Task SyncRunAtStartupRegistrationAsync()
    {
        try
        {
            var shouldRunAtStartup = await _dataOperationRunner.RunAsync(
                "resolve run-at-startup setting",
                async (scope, ct) =>
                {
                    var setting = await scope.UnitOfWork.UserSettings.GetByNameAsync(UserSettingNames.ShouldRunAtStartup, ct);
                    return UserSettingValueParser.ParseBool(setting?.Value, false);
                });

            _startupRegistrationService.SetRunAtStartup(shouldRunAtStartup);
        }
        catch (Exception exception)
        {
            FluxoLogManager.LogWarning(
                exception,
                "Unable to sync Windows startup registration from user settings.");
        }
    }

    private void EnsureTrayIconInitialized()
    {
        if (_trayIcon is not null)
            return;

        var processPath = Environment.ProcessPath;
        var icon = !string.IsNullOrWhiteSpace(processPath)
            ? System.Drawing.Icon.ExtractAssociatedIcon(processPath) ?? System.Drawing.SystemIcons.Application
            : System.Drawing.SystemIcons.Application;

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = icon,
            Text = "Fluxo",
            Visible = true
        };
        _trayIcon.MouseClick += OnTrayIconMouseClick;
        _trayIcon.MouseDoubleClick += OnTrayIconMouseDoubleClick;

        EnsureTrayMenuPopupInitialized();
    }

    private void EnsureTrayMenuPopupInitialized()
    {
        if (_trayMenuPopup is not null)
            return;

        _trayMenuPopup = new TrayMenuPopup();
        _trayMenuPopup.OpenFluxoRequested += (_, _) => RestoreMainWindowFromTray();
        _trayMenuPopup.RestartFluxoRequested += (_, _) => RestartFromTray();
        _trayMenuPopup.ExitRequested += (_, _) => ExitFromTray();
    }

    private bool IsMainWindowHiddenToTray()
    {
        return MainWindow is MainWindow mainWindow
               && !mainWindow.IsVisible
               && !mainWindow.ShowInTaskbar;
    }

    private void OnTrayIconMouseClick(object? sender, Forms.MouseEventArgs e)
    {
        RunOnUiThread(() =>
        {
            if (e.Button == Forms.MouseButtons.Right)
            {
                _trayLeftClickTimer.Stop();
                _isTrayLeftClickPending = false;
                ShowTrayMenuPopup();
                return;
            }

            if (e.Button != Forms.MouseButtons.Left)
                return;

            _isTrayLeftClickPending = true;
            _trayLeftClickTimer.Stop();
            _trayLeftClickTimer.Start();
        });
    }

    private void OnTrayIconMouseDoubleClick(object? sender, Forms.MouseEventArgs e)
    {
        RunOnUiThread(() =>
        {
            if (e.Button != Forms.MouseButtons.Left)
                return;

            _trayLeftClickTimer.Stop();
            _isTrayLeftClickPending = false;
            _trayMenuPopup?.Hide();
            RestoreMainWindowFromTray();
        });
    }

    private void OnTrayLeftClickTimerTick(object? sender, EventArgs e)
    {
        _trayLeftClickTimer.Stop();

        if (!_isTrayLeftClickPending)
            return;

        _isTrayLeftClickPending = false;
        ShowTrayMenuPopup();
    }

    private void ShowTrayMenuPopup()
    {
        EnsureTrayMenuPopupInitialized();
        if (_trayMenuPopup is null)
            return;

        var cursorPosition = Forms.Cursor.Position;
        _trayMenuPopup.ShowNearScreenPoint(new System.Windows.Point(cursorPosition.X, cursorPosition.Y));
    }

    private void HideMainWindowToTray(MainWindow mainWindow)
    {
        _trayMenuPopup?.Hide();
        mainWindow.HideToTray();
    }

    private void RestoreMainWindowFromTray()
    {
        _trayMenuPopup?.Hide();

        if (MainWindow is MainWindow mainWindow)
            mainWindow.ShowFromTray();
    }

    private void RestartFromTray()
    {
        try
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath))
                throw new InvalidOperationException("Unable to resolve the current executable path.");

            Process.Start(new ProcessStartInfo
            {
                FileName = processPath,
                Arguments = RestartTrayArgument,
                UseShellExecute = true
            });

            ExitFromTray();
        }
        catch (Exception exception)
        {
            FluxoLogManager.LogFailureForProcess(exception, "restart Fluxo from tray");

            if (_serviceProvider?.GetService<IDialogService>() is { } dialogService)
                dialogService.ShowError(FluxoLogManager.CreateFailureMessage("restart Fluxo"), "Fluxo");
            else
                FluxoMessageBox.Show(null, FluxoLogManager.CreateFailureMessage("restart Fluxo"), "Fluxo",
                    MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExitFromTray()
    {
        _isForcedShutdownRequested = true;
        _trayMenuPopup?.Hide();

        if (MainWindow is MainWindow mainWindow)
            mainWindow.Close();
        else
            Shutdown();
    }

    private void DisposeTrayResources()
    {
        _trayLeftClickTimer.Stop();
        _isTrayLeftClickPending = false;

        if (_trayIcon is not null)
        {
            _trayIcon.MouseClick -= OnTrayIconMouseClick;
            _trayIcon.MouseDoubleClick -= OnTrayIconMouseDoubleClick;
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        if (_trayMenuPopup is not null)
        {
            _trayMenuPopup.Close();
            _trayMenuPopup = null;
        }

    }

    private void RunOnUiThread(Action action)
    {
        if (Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.BeginInvoke(action);
    }

    private static async Task<bool> EnsureFirstRunSettingAsync(IDataOperationRunner dataOperationRunner)
    {
        return await dataOperationRunner.RunAsync("ensure first-run setting", async (scope, ct) =>
        {
            var existingSetting = await scope.UnitOfWork.UserSettings.GetByNameAsync(UserSettingNames.IsFirstRun, ct);

            if (existingSetting is null)
            {
                await scope.UnitOfWork.UserSettings.AddAsync(new UserSettings
                {
                    Name = UserSettingNames.IsFirstRun,
                    Value = bool.TrueString
                }, ct);
                await scope.UnitOfWork.SaveChangesAsync(ct);
                return true;
            }

            return !bool.TryParse(existingSetting.Value, out var isFirstRun) || isFirstRun;
        });
    }

    internal static Task MigrateDatabaseAsync(IDataOperationRunner dataOperationRunner)
    {
        return MigrateDatabaseAsync(dataOperationRunner, FluxoDbContextFactory.GetDatabasePath);
    }

    internal static Task MigrateDatabaseAsync(
        IDataOperationRunner dataOperationRunner,
        Func<string> databasePathProvider)
    {
        return dataOperationRunner.RunAsync("migrate database", async (scope, ct) =>
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FluxoDbContext>();
            var databasePath = databasePathProvider();
            var databaseExists = File.Exists(databasePath);
            var allMigrations = dbContext.Database.GetMigrations().ToList();

            if (!databaseExists)
            {
                await CreateCurrentSchemaAndSeedMigrationHistoryAsync(dbContext, allMigrations, ct);
                return;
            }

            var appliedMigrations = await TryGetAppliedMigrationsAsync(dbContext, ct);
            await NormalizeLegacyAccountSchemaAsync(dbContext, allMigrations, appliedMigrations, ct);
            appliedMigrations = await TryGetAppliedMigrationsAsync(dbContext, ct);

            if (appliedMigrations.Count == 0)
            {
                var hasAnyApplicationTable = await HasAnyApplicationTableAsync(dbContext, ct);
                if (!hasAnyApplicationTable)
                {
                    await CreateCurrentSchemaAndSeedMigrationHistoryAsync(dbContext, allMigrations, ct);
                    return;
                }

                var inferredLatestMigration = await InferLatestAppliedMigrationAsync(dbContext, allMigrations, ct);

                if (!string.IsNullOrWhiteSpace(inferredLatestMigration))
                {
                    await EnsureMigrationHistoryTableAsync(dbContext, ct);
                    await SeedMigrationHistoryAsync(dbContext, allMigrations, inferredLatestMigration, ct);
                    appliedMigrations = await TryGetAppliedMigrationsAsync(dbContext, ct);
                }
            }

            await dbContext.Database.MigrateAsync(ct);
        });
    }

    private static async Task NormalizeLegacyAccountSchemaAsync(
        FluxoDbContext dbContext,
        IReadOnlyList<string> allMigrations,
        IReadOnlyCollection<string> appliedMigrations,
        CancellationToken cancellationToken)
    {
        var legacyEntityName = "Spending" + "Source";
        var legacyTableName = legacyEntityName + "s";
        var hasLegacyTable = await TableExistsAsync(dbContext, legacyTableName, cancellationToken);
        var hasCurrentTable = await TableExistsAsync(dbContext, "Accounts", cancellationToken);

        if (hasLegacyTable && !hasCurrentTable)
        {
            await dbContext.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=OFF;", cancellationToken);
            try
            {
                var renameTableSql = "ALTER TABLE \"" + legacyTableName + "\" RENAME TO \"Accounts\";";
                await dbContext.Database.ExecuteSqlRawAsync(renameTableSql, cancellationToken);
                await RenameColumnIfExistsAsync(dbContext, "Accounts", legacyEntityName + "Type", "AccountType",
                    cancellationToken);
                await RenameColumnIfExistsAsync(dbContext, "Expenses", legacyEntityName + "Id", "AccountId",
                    cancellationToken);
                await RenameColumnIfExistsAsync(dbContext, "ExpenseLogs", legacyEntityName + "Id", "AccountId",
                    cancellationToken);
                await RenameColumnIfExistsAsync(dbContext, "IncomeLogs", legacyEntityName + "Id", "AccountId",
                    cancellationToken);
            }
            finally
            {
                await dbContext.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;", cancellationToken);
            }
        }

        if (appliedMigrations.Count == 0)
            return;

        await RemapLegacyAccountMigrationHistoryAsync(dbContext, allMigrations, cancellationToken);
    }

    private static async Task RenameColumnIfExistsAsync(
        FluxoDbContext dbContext,
        string tableName,
        string legacyColumnName,
        string currentColumnName,
        CancellationToken cancellationToken)
    {
        if (!await ColumnExistsAsync(dbContext, tableName, legacyColumnName, cancellationToken) ||
            await ColumnExistsAsync(dbContext, tableName, currentColumnName, cancellationToken))
            return;

        var renameColumnSql = "ALTER TABLE \"" + tableName + "\" RENAME COLUMN \"" + legacyColumnName +
                              "\" TO \"" + currentColumnName + "\";";
        await dbContext.Database.ExecuteSqlRawAsync(renameColumnSql, cancellationToken);
    }

    private static async Task RemapLegacyAccountMigrationHistoryAsync(
        FluxoDbContext dbContext,
        IReadOnlyCollection<string> allMigrations,
        CancellationToken cancellationToken)
    {
        var legacyEntityName = "Spending" + "Source";
        var legacyMigrationPairs = new[]
        {
            ("20260401093922_AddShowOnUITo" + legacyEntityName, "20260401093922_AddShowOnUIToAccount"),
            ("20260411142128_AddIsEnabledTo" + legacyEntityName, "20260411142128_AddIsEnabledToAccount"),
            ("20260415014219_AddIsForDeletionTo" + legacyEntityName, "20260415014219_AddIsForDeletionToAccount"),
            ("20260416153000_AddIconNameAndMonthlyDueDateTo" + legacyEntityName,
                "20260416153000_AddIconNameAndMonthlyDueDateToAccount")
        };

        foreach (var (legacyMigrationId, currentMigrationId) in legacyMigrationPairs)
        {
            if (!allMigrations.Contains(currentMigrationId))
                continue;

            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT OR IGNORE INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                 SELECT {currentMigrationId}, "ProductVersion"
                 FROM "__EFMigrationsHistory"
                 WHERE "MigrationId" = {legacyMigrationId};
                 """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 DELETE FROM "__EFMigrationsHistory"
                 WHERE "MigrationId" = {legacyMigrationId}
                   AND EXISTS (
                       SELECT 1
                       FROM "__EFMigrationsHistory"
                       WHERE "MigrationId" = {currentMigrationId}
                   );
                 """,
                cancellationToken);
        }
    }

    private static async Task CreateCurrentSchemaAndSeedMigrationHistoryAsync(
        FluxoDbContext dbContext,
        IReadOnlyList<string> allMigrations,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.EnsureDeletedAsync(cancellationToken);
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        await SeedCurrentSchemaDataAsync(dbContext, cancellationToken);
        await EnsureMigrationHistoryTableAsync(dbContext, cancellationToken);

        if (allMigrations.Count > 0)
            await SeedMigrationHistoryAsync(dbContext, allMigrations, allMigrations[^1], cancellationToken);
    }

    private static async Task SeedCurrentSchemaDataAsync(
        FluxoDbContext dbContext,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "Tags" ("Name", "HexCode", "IsSystemTag")
            SELECT 'Data Restoration', '#e9c178', 1
            WHERE NOT EXISTS (
                SELECT 1 FROM "Tags"
                WHERE "Name" = 'Data Restoration' AND "IsSystemTag" = 1
            );

            INSERT INTO "Tags" ("Name", "HexCode", "IsSystemTag")
            SELECT 'Budget Reconciliation', '#f0ebbe', 1
            WHERE NOT EXISTS (
                SELECT 1 FROM "Tags"
                WHERE "Name" = 'Budget Reconciliation' AND "IsSystemTag" = 1
            );
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "BudgetAllocation" (
                "NeedsThreshold",
                "WantsThreshold",
                "InvestThreshold",
                "AllocationPeriod",
                "PeriodStart",
                "CurrentPeriodIndex",
                "LastRolloverPeriodStart",
                "AllocationLimit",
                "NeedsDebt",
                "WantsDebt",
                "InvestDebt",
                "RolloverPolicy",
                "OverspendPolicy")
            SELECT 50, 30, 20, 2, 1, 1, '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0
            WHERE NOT EXISTS (
                SELECT 1
                FROM "BudgetAllocation"
            );
            """,
            cancellationToken);
    }

    private async Task SyncBudgetAllocationPeriodAsync()
    {
        await _dataOperationRunner.RunAsync(
            "sync budget allocation period",
            async (scope, ct) =>
            {
                await _budgetAllocationPeriodSyncService.SyncAsync(scope.UnitOfWork, ct);
            });
    }

    private static async Task<List<string>> TryGetAppliedMigrationsAsync(
        FluxoDbContext dbContext,
        CancellationToken cancellationToken)
    {
        try
        {
            return (await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
        }
        catch (SqliteException exception) when (
            exception.SqliteErrorCode == 1 &&
            exception.Message.Contains("__EFMigrationsHistory", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }
    }

    private static async Task EnsureMigrationHistoryTableAsync(
        FluxoDbContext dbContext,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                "ProductVersion" TEXT NOT NULL
            );
            """,
            cancellationToken);
    }

    private static async Task SeedMigrationHistoryAsync(
        FluxoDbContext dbContext,
        IReadOnlyList<string> allMigrations,
        string latestAppliedMigration,
        CancellationToken cancellationToken)
    {
        var latestIndex = allMigrations
            .ToList()
            .FindIndex(migrationId => string.Equals(migrationId, latestAppliedMigration, StringComparison.Ordinal));
        if (latestIndex < 0)
            return;

        var efCoreVersion = typeof(DbContext).Assembly.GetName().Version;
        var productVersion = efCoreVersion is null
            ? "10.0.0"
            : $"{efCoreVersion.Major}.{efCoreVersion.Minor}.{Math.Max(0, efCoreVersion.Build)}";

        for (var index = 0; index <= latestIndex; index++)
        {
            var migrationId = allMigrations[index];
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT OR IGNORE INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                 VALUES ({migrationId}, {productVersion});
                 """,
                cancellationToken);
        }
    }

    private static async Task<string?> InferLatestAppliedMigrationAsync(
        FluxoDbContext dbContext,
        IReadOnlyList<string> allMigrations,
        CancellationToken cancellationToken)
    {
        if (allMigrations.Count == 0)
            return null;

        if (await ColumnExistsAsync(dbContext, "RecurringTransactions", "IsExcludedFromBudget", cancellationToken))
            return "20260713093244_AddRecurringBudgetExclusionAndRepairTransactionTags";

        var hasRelatedRecurringTransaction = await ColumnExistsAsync(
            dbContext, "Transactions", "RelatedRecurringTransactionId", cancellationToken);
        if (hasRelatedRecurringTransaction &&
            !await ColumnExistsAsync(dbContext, "Transactions", "IsExcludedFromBudget", cancellationToken))
            return allMigrations[^1];

        if (hasRelatedRecurringTransaction)
            return "20260710233033_RemoveNotificationsAndAddRelatedRecurringTransaction";

        var hasCreatedOnColumn = await ColumnExistsAsync(dbContext, "SavingGoals", "CreatedOn", cancellationToken);
        if (hasCreatedOnColumn)
            return "20260421180000_AddSavingGoalCreatedOn";

        var hasMonthlyDueDate = await ColumnExistsAsync(dbContext, "Accounts", "MonthlyDueDate", cancellationToken);
        var hasIsSystemTag = await ColumnExistsAsync(dbContext, "Tags", "IsSystemTag", cancellationToken);
        var hasDeductSource = await ColumnExistsAsync(dbContext, "Accounts", "DeductSource", cancellationToken);
        var hasNotificationsTable = await TableExistsAsync(dbContext, "Notifications", cancellationToken);
        var hasIconName = await ColumnExistsAsync(dbContext, "Tags", "IconName", cancellationToken);

        if (hasMonthlyDueDate && hasIsSystemTag && hasDeductSource && hasNotificationsTable && !hasIconName)
            return "20260421165000_RemoveIconNameFromTag";

        if (hasMonthlyDueDate && hasIsSystemTag && hasDeductSource && hasNotificationsTable)
        {
            var accountLimitType = await GetColumnTypeAsync(dbContext, "Accounts", "AccountLimit", cancellationToken);
            if (string.Equals(accountLimitType, "NUMERIC", StringComparison.OrdinalIgnoreCase))
                return "20260419153000_ConvertMoneyColumnsToNumeric";

            return "20260419120000_AddNotificationsAndDeductSource";
        }

        if (hasMonthlyDueDate && hasIsSystemTag)
            return "20260417064811_AddIsSystemTagToTag";

        if (hasMonthlyDueDate)
        {
            var monthlyDueDateNullable = await IsColumnNullableAsync(dbContext, "Accounts", "MonthlyDueDate", cancellationToken);
            if (monthlyDueDateNullable is true)
                return "20260416170000_MakeMonthlyDueDateNullable";

            return "20260416153000_AddIconNameAndMonthlyDueDateToAccount";
        }

        if (await ColumnExistsAsync(dbContext, "Accounts", "IsForDeletion", cancellationToken))
            return "20260415014219_AddIsForDeletionToAccount";

        if (await ColumnExistsAsync(dbContext, "Accounts", "IsEnabled", cancellationToken))
            return "20260411142128_AddIsEnabledToAccount";

        if (await ColumnExistsAsync(dbContext, "ExpenseLogs", "IsForDeletion", cancellationToken))
            return "20260408110007_AddIsForDeletionToExpenseLog";

        if (await TableExistsAsync(dbContext, "UserSettings", cancellationToken))
            return "20260408105340_AddUserSettings";

        if (await ColumnExistsAsync(dbContext, "Accounts", "ShowOnUI", cancellationToken))
            return "20260401093922_AddShowOnUIToAccount";

        return null;
    }

    private static async Task<bool> HasAnyApplicationTableAsync(
        FluxoDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var tableResult = await ExecuteScalarAsync(
            dbContext,
            """
            SELECT 1
            FROM sqlite_master
            WHERE type = 'table'
              AND name NOT LIKE 'sqlite_%'
              AND name NOT LIKE '__EFMigrations%'
            LIMIT 1;
            """,
            cancellationToken);

        return tableResult is not null;
    }

    private static async Task<bool> TableExistsAsync(
        FluxoDbContext dbContext,
        string tableName,
        CancellationToken cancellationToken)
    {
        var tableResult = await ExecuteScalarAsync(
            dbContext,
            "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1;",
            ("$name", tableName),
            cancellationToken);

        return tableResult is not null;
    }

    private static async Task<bool> ColumnExistsAsync(
        FluxoDbContext dbContext,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        var tableExists = await TableExistsAsync(dbContext, tableName, cancellationToken);
        if (!tableExists)
            return false;

        await using var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{tableName}\");";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.IsDBNull(1))
                continue;

            var name = reader.GetString(1);
            if (string.Equals(name, columnName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static async Task<string?> GetColumnTypeAsync(
        FluxoDbContext dbContext,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        var tableExists = await TableExistsAsync(dbContext, tableName, cancellationToken);
        if (!tableExists)
            return null;

        await using var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{tableName}\");";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.IsDBNull(1))
                continue;

            var name = reader.GetString(1);
            if (!string.Equals(name, columnName, StringComparison.OrdinalIgnoreCase))
                continue;

            return reader.IsDBNull(2) ? null : reader.GetString(2);
        }

        return null;
    }

    private static async Task<bool?> IsColumnNullableAsync(
        FluxoDbContext dbContext,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        var tableExists = await TableExistsAsync(dbContext, tableName, cancellationToken);
        if (!tableExists)
            return null;

        await using var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{tableName}\");";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.IsDBNull(1))
                continue;

            var name = reader.GetString(1);
            if (!string.Equals(name, columnName, StringComparison.OrdinalIgnoreCase))
                continue;

            // PRAGMA table_info notnull: 0 means nullable, 1 means not nullable.
            var notNull = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
            return notNull == 0;
        }

        return null;
    }

    private static async Task<object?> ExecuteScalarAsync(
        FluxoDbContext dbContext,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task<object?> ExecuteScalarAsync(
        FluxoDbContext dbContext,
        string commandText,
        (string Name, object? Value) parameter,
        CancellationToken cancellationToken)
    {
        await using var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        AddParameter(command, parameter.Name, parameter.Value);
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private async Task InitializeLoggingAsync()
    {
        string? username = null;

        try
        {
            username = await _dataOperationRunner.RunAsync("resolve app username for logging", async (scope, ct) =>
            {
                var userSetting = await scope.UnitOfWork.UserSettings
                    .GetByNameAsync(UserSettingNames.PreferredDisplayName, ct);
                return userSetting?.Value;
            });
        }
        catch
        {
            // Falls through to default username initialization.
        }

        try
        {
            FluxoLogManager.Initialize(username);
        }
        catch (Exception exception)
        {
            try
            {
                FluxoLogManager.Initialize("User");
                FluxoLogManager.LogFailureForProcess(
                    exception,
                    "initialize Serilog with the configured app username");
            }
            catch
            {
                // Startup continues even if logging initialization fails.
            }
        }
    }

    private async Task BackupDatabaseOnStartupAsync()
    {
        var databasePath = FluxoDbContextFactory.GetDatabasePath();
        var databaseDirectoryPath = Path.GetDirectoryName(databasePath);
        if (string.IsNullOrWhiteSpace(databaseDirectoryPath))
            return;

        var backupDirectoryPath = Path.Combine(databaseDirectoryPath, "backup");

        try
        {
            Directory.CreateDirectory(databaseDirectoryPath);
            Directory.CreateDirectory(backupDirectoryPath);

            if (!File.Exists(databasePath))
                return;

            if (await ShouldSkipDatabaseBackupForFirstRunAsync())
                return;

            var username = await TryResolveBackupUsernameAsync();
            var safeUsername = SanitizeBackupToken(username);
            var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            var backupFileName = $"{safeUsername}_{timestamp}_backup.db";
            var backupPath = Path.Combine(backupDirectoryPath, backupFileName);

            File.Copy(databasePath, backupPath, overwrite: true);
            PruneExpiredBackups(backupDirectoryPath);
        }
        catch (Exception exception)
        {
            FluxoLogManager.LogWarning(exception, "Unable to create startup database backup.");
        }
    }

    private async Task<bool> ShouldSkipDatabaseBackupForFirstRunAsync()
    {
        try
        {
            return await _dataOperationRunner.RunAsync("check first-run before database backup", async (scope, ct) =>
            {
                var setting = await scope.UnitOfWork.UserSettings.GetByNameAsync(UserSettingNames.IsFirstRun, ct);
                if (setting is null)
                    return true;

                return !bool.TryParse(setting.Value, out var isFirstRun) || isFirstRun;
            });
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task<string?> TryResolveBackupUsernameAsync()
    {
        try
        {
            return await _dataOperationRunner.RunAsync("resolve app username for database backup", async (scope, ct) =>
            {
                var userSetting = await scope.UnitOfWork.UserSettings
                    .GetByNameAsync(UserSettingNames.PreferredDisplayName, ct);
                return userSetting?.Value;
            });
        }
        catch (Exception exception)
        {
            FluxoLogManager.LogWarning(
                exception,
                "Unable to resolve app username for startup database backup. Falling back to default username.");
            return null;
        }
    }

    private static string SanitizeBackupToken(string? username)
    {
        const string defaultUsername = "User";
        var rawUsername = string.IsNullOrWhiteSpace(username) ? defaultUsername : username.Trim();
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitizedCharacters = rawUsername.Where(character => !invalidCharacters.Contains(character)).ToArray();
        var sanitizedUsername = new string(sanitizedCharacters).Trim();
        return string.IsNullOrWhiteSpace(sanitizedUsername) ? defaultUsername : sanitizedUsername;
    }

    private static void PruneExpiredBackups(string backupDirectoryPath)
    {
        var retentionCutoffUtc = DateTime.UtcNow.AddDays(-3);
        var backupFiles = Directory.EnumerateFiles(backupDirectoryPath, "*_backup.db");

        foreach (var backupFile in backupFiles)
        {
            try
            {
                var createdAtUtc = File.GetCreationTimeUtc(backupFile);
                if (createdAtUtc <= retentionCutoffUtc)
                    File.Delete(backupFile);
            }
            catch (Exception exception)
            {
                FluxoLogManager.LogWarning(exception, $"Unable to prune expired backup file: {backupFile}");
            }
        }
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private static void InitializeBootstrapLogging()
    {
        try
        {
            FluxoLogManager.Initialize("User");
            LogStartupStage("bootstrap logging", StartupStageState.Completed);
        }
        catch
        {
            // No UI or DI exists yet. Startup continues so later handlers can report failures.
        }
    }

    private static void LogStartupStage(string stage, StartupStageState state)
    {
        try
        {
            FluxoLogManager.LogInformation($"Startup {state}: {stage}");
        }
        catch
        {
            // Startup diagnostics must never become the startup failure.
        }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        FluxoLogManager.LogError(e.Exception, "Unhandled dispatcher exception.");
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            FluxoLogManager.LogError(
                exception,
                $"Unhandled app domain exception. IsTerminating={e.IsTerminating}");
            return;
        }

        FluxoLogManager.LogInformation(
            $"Unhandled app domain exception object (non-Exception). IsTerminating={e.IsTerminating}");
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        FluxoLogManager.LogError(e.Exception, "Unobserved task exception.");
        e.SetObserved();
    }
}
