using CommunityToolkit.Mvvm.ComponentModel;
using Fluxo.Helpers.Settings;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Constants;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Core.Interfaces.Operations;
using Fluxo.Data.Context;
using Fluxo.Services.Logging;
using Fluxo.Services.Persistence;
using Fluxo.Services.Caching;
using Fluxo.ViewModels.Popups.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.ExceptionServices;
using Fluxo.Resources.Resources.Messages;
using MainVM = Fluxo.ViewModels.Shell.Main.MainVM;

namespace Fluxo.ViewModels.Shell.QuickSetupWizard;

public partial class QuickSetupWizardVM : ObservableRecipient,
    IRecipient<QuickSetupWizardAccountsChangedMessage>,
    IRecipient<QuickSetupWizardBudgetAllocationChangedMessage>
{
    private readonly MainVM _mainViewModel;
    private readonly IAppDataService _appData;
    private readonly IStartupRegistrationService _startupRegistrationService;
    private readonly IDataOperationScopeFactory _dataOperationScopeFactory;
    private readonly AppDataCommitCoordinator _appDataCommitCoordinator;
    private IDataOperationScope? _stagedScope;
    private Func<Task>? _stagedCommitAsync;
    private Func<Task>? _stagedRollbackAsync;

    [ObservableProperty] private int _currentStepIndex;
    [ObservableProperty] private bool _hasAccounts;

    public QuickSetupWizardVM(
        MainVM mainViewModel,
        IAppDataService appData,
        IStartupRegistrationService startupRegistrationService,
        IDataOperationScopeFactory dataOperationScopeFactory,
        AppDataCommitCoordinator appDataCommitCoordinator,
        QuickSetupWizardGreetingPageVM greetingPage,
        QuickSetupWizardNamePageVM namePage,
        QuickSetupWizardMiddlePageVM middlePage,
        QuickSetupWizardLoadingPageVM loadingPage,
        QuickSetupWizardFinalPageVM finalPage,
        IMessenger? messenger = null)
        : base(messenger ?? WeakReferenceMessenger.Default)
    {
        _mainViewModel = mainViewModel;
        _appData = appData;
        _startupRegistrationService = startupRegistrationService;
        _dataOperationScopeFactory = dataOperationScopeFactory;
        _appDataCommitCoordinator = appDataCommitCoordinator;

        GreetingPage = greetingPage;
        NamePage = namePage;
        MiddlePage = middlePage;
        LoadingPage = loadingPage;
        FinalPage = finalPage;

        IsActive = true;
    }

    public QuickSetupWizardGreetingPageVM GreetingPage { get; }

    public QuickSetupWizardNamePageVM NamePage { get; }

    public QuickSetupWizardMiddlePageVM MiddlePage { get; }

    public QuickSetupWizardLoadingPageVM LoadingPage { get; }

    public QuickSetupWizardFinalPageVM FinalPage { get; }

    public int TotalSteps => QuickSetupWizardShared.TotalSteps;

    public int CurrentStep => CurrentStepIndex + 1;

    public bool IsGreetingStep => CurrentStepIndex == 0;

    public bool IsNameStep => CurrentStepIndex == 1;

    public bool IsMiddleStep => CurrentStepIndex is >= 2 and <= 8;

    public bool IsLoadingStep => CurrentStepIndex == 9;

    public bool IsFinalStep => CurrentStepIndex == TotalSteps - 1;

    public bool IsStep2Active => CurrentStepIndex == 2;

    public bool IsNextEnabled => !(CurrentStepIndex == 5 && MiddlePage.BudgetAllocation.HasBudgetAllocationError);

    partial void OnCurrentStepIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsGreetingStep));
        OnPropertyChanged(nameof(IsNameStep));
        OnPropertyChanged(nameof(IsMiddleStep));
        OnPropertyChanged(nameof(IsLoadingStep));
        OnPropertyChanged(nameof(IsFinalStep));
        OnPropertyChanged(nameof(IsStep2Active));
        OnPropertyChanged(nameof(IsNextEnabled));
        OnPropertyChanged(nameof(CurrentStep));

        if (value is >= 2 and <= 8)
            MiddlePage.SetCurrentStepIndex(value);
    }

    public void Receive(QuickSetupWizardAccountsChangedMessage message)
    {
        HasAccounts = message.Value.HasAny;
    }

    public void Receive(QuickSetupWizardBudgetAllocationChangedMessage message)
    {
        OnPropertyChanged(nameof(IsNextEnabled));
    }

    public async Task LoadAsync()
    {
        await NamePage.LoadAsync();
        await MiddlePage.LoadAsync();
    }

    public void GoBack()
    {
        if (CurrentStepIndex <= 0)
            return;

        if (IsFinalStep)
        {
            CurrentStepIndex = 7;
            return;
        }

        if (CurrentStepIndex == 6 && !HasAccounts)
        {
            CurrentStepIndex = 2;
            return;
        }

        CurrentStepIndex--;
    }

    public void NavigateToStep(int stepIndex)
    {
        if (stepIndex >= 0 && stepIndex < TotalSteps && stepIndex != CurrentStepIndex)
            CurrentStepIndex = stepIndex;
    }

    public async Task<SettingsOperationResult> GoNextAsync()
    {
        var result = await PersistCurrentStepAsync();
        if (!result.IsSuccess)
            return result;

        if (CurrentStepIndex < TotalSteps - 1)
            CurrentStepIndex++;

        return SettingsOperationResult.Success();
    }

    public async Task InitializeMainViewModelAsync()
    {
        await _mainViewModel.Initialize();
    }

    public Task<QuickSetupWizardLoadingOutcome> ExecuteLoadingFlowAsync(
        Func<Task<bool>>? tryStageAsyncOverride,
        Func<Task<bool>> confirmRetryCycleAsync,
        Func<TimeSpan, Task>? delayAsync = null)
    {
        return QuickSetupWizardLoadingCoordinator.RunAsync(
            tryStageAsyncOverride ?? TryStageDraftAsync,
            confirmRetryCycleAsync,
            delayAsync ?? (duration => Task.Delay(duration)));
    }

    public async Task<SettingsOperationResult> CompleteAsync()
    {
        Exception? capturedException = null;

        try
        {
            if (_stagedCommitAsync is not null)
                await _stagedCommitAsync();

            await SaveIsFirstRunAsync(false);
            try
            {
                FluxoLogManager.Initialize(NamePage.ResolvedUsername);
            }
            catch (Exception loggerException)
            {
                FluxoLogManager.LogWarning(
                    loggerException,
                    "Failed to rotate log file after startup wizard username resolution.");
            }
            await SyncRunAtStartupRegistrationAsync();

            if (!_mainViewModel.IsInitialized)
                await _mainViewModel.Initialize();
            else
                await _mainViewModel.ReloadCurrentDataAsync();
        }
        catch (Exception exception)
        {
            FluxoLogManager.LogError(exception, "Failed to finalize startup wizard setup.");
            capturedException = exception;
        }

        try
        {
            await ClearStagedAsync(rollbackStagedChanges: false);
        }
        catch (Exception exception)
        {
            FluxoLogManager.LogError(exception, "Failed to clear staged startup wizard state after setup completion.");
            capturedException ??= exception;
        }

        return capturedException is null
            ? SettingsOperationResult.Success()
            : SettingsOperationResult.Failure(CreateQuickSetupWizardErrorMessage("finish setup", capturedException));
    }

    public async Task<SettingsOperationResult> DismissAsync()
    {
        try
        {
            await ClearStagedAsync();
            await SaveIsFirstRunAsync(false);

            if (!_mainViewModel.IsInitialized)
                await _mainViewModel.Initialize();
            else
                await _mainViewModel.ReloadCurrentDataAsync();
        }
        catch (Exception exception)
        {
            FluxoLogManager.LogError(exception, "Failed to dismiss startup wizard.");
            return SettingsOperationResult.Failure(
                CreateQuickSetupWizardErrorMessage("close the startup wizard", exception));
        }

        return SettingsOperationResult.Success();
    }

    private async Task<SettingsOperationResult> PersistCurrentStepAsync()
    {
        return await Task.FromResult(SettingsOperationResult.Success());
    }

    private async Task<bool> TryStageDraftAsync()
    {
        IDataOperationScope? scope = null;
        IDbContextTransaction? transaction = null;

        try
        {
            await ClearStagedAsync();

            scope = await _dataOperationScopeFactory.CreateAsync();
            var dbContext = scope.ServiceProvider.GetRequiredService<FluxoDbContext>();
            transaction = await dbContext.Database.BeginTransactionAsync();
            var stagedTransaction = transaction ??
                throw new InvalidOperationException("Unable to begin startup wizard staging transaction.");

            var stagedAppData = new AppDataService(scope.UnitOfWork);
            await NamePage.ApplyAsync(stagedAppData);
            var budgetAllocationResult = await MiddlePage.BudgetAllocation.ApplyAsync(stagedAppData);
            if (!budgetAllocationResult.IsSuccess)
                throw new InvalidOperationException(budgetAllocationResult.ErrorMessage);

            await MiddlePage.Personalization.ApplyAsync(stagedAppData);
            await MiddlePage.Notification.ApplyAsync(stagedAppData);
            await MiddlePage.Accounts.ApplyAsync(stagedAppData);
            await MiddlePage.RecurringTransactions.ApplyAsync(
                stagedAppData,
                MiddlePage.Accounts.LastPersistedIdMap);
            await MiddlePage.SavingGoals.ApplyAsync(stagedAppData);
            await stagedAppData.SaveChangesAsync();

            _stagedScope = scope;
            _stagedCommitAsync = () => CommitStagedDataAsync(
                () => ExecuteTransactionActionAndDisposeAsync(stagedTransaction, tx => tx.CommitAsync()),
                () => _appDataCommitCoordinator.RebuildAsync());
            _stagedRollbackAsync = async () =>
            {
                await ExecuteTransactionActionAndDisposeAsync(stagedTransaction, tx => tx.RollbackAsync());
            };

            scope = null;
            transaction = null;
            return true;
        }
        catch (Exception exception)
        {
            FluxoLogManager.LogWarning(exception, "Startup wizard draft staging failed. Retry flow will be offered.");
            await DisposeQuietlyAsync(transaction);
            await DisposeQuietlyAsync(scope);
            await ClearStagedQuietlyAsync();
            return false;
        }
    }

    private static async Task DisposeQuietlyAsync(IAsyncDisposable? disposable)
    {
        if (disposable is null)
            return;

        try
        {
            await disposable.DisposeAsync();
        }
        catch (Exception exception)
        {
            FluxoLogManager.LogWarning(exception, "Failed to dispose staging resources quietly.");
            // Staging failures should surface as a retryable loading attempt.
        }
    }

    private async Task ClearStagedQuietlyAsync()
    {
        try
        {
            await ClearStagedAsync();
        }
        catch (Exception exception)
        {
            FluxoLogManager.LogWarning(exception, "Failed to clear staged startup wizard data quietly.");
            _stagedScope = null;
            _stagedCommitAsync = null;
            _stagedRollbackAsync = null;
        }
    }

    private static async Task ExecuteTransactionActionAndDisposeAsync(
        IDbContextTransaction transaction,
        Func<IDbContextTransaction, Task> actionAsync)
    {
        ExceptionDispatchInfo? capturedException = null;

        try
        {
            await actionAsync(transaction);
        }
        catch (Exception exception)
        {
            FluxoLogManager.LogError(exception, "Failed to execute staged transaction action.");
            capturedException = ExceptionDispatchInfo.Capture(exception);
        }

        try
        {
            await transaction.DisposeAsync();
        }
        catch (Exception exception)
        {
            FluxoLogManager.LogError(exception, "Failed to dispose staged transaction.");
            capturedException ??= ExceptionDispatchInfo.Capture(exception);
        }

        capturedException?.Throw();
    }

    internal static async Task CommitStagedDataAsync(Func<Task> commitAsync, Func<Task> rebuildCacheAsync)
    {
        await commitAsync();
        await rebuildCacheAsync();
    }

    private async Task ClearStagedAsync(bool rollbackStagedChanges = true)
    {
        var stagedScope = _stagedScope;
        var stagedRollbackAsync = _stagedRollbackAsync;
        ExceptionDispatchInfo? capturedException = null;

        _stagedScope = null;
        _stagedCommitAsync = null;
        _stagedRollbackAsync = null;

        try
        {
            if (rollbackStagedChanges && stagedRollbackAsync is not null)
                await stagedRollbackAsync();
        }
        catch (Exception exception)
        {
            FluxoLogManager.LogError(exception, "Failed to rollback staged startup wizard changes.");
            capturedException = ExceptionDispatchInfo.Capture(exception);
        }

        try
        {
            if (stagedScope is not null)
                await stagedScope.DisposeAsync();
        }
        catch (Exception exception)
        {
            FluxoLogManager.LogError(exception, "Failed to dispose staged startup wizard scope.");
            capturedException ??= ExceptionDispatchInfo.Capture(exception);
        }

        capturedException?.Throw();
    }

    private static string CreateQuickSetupWizardErrorMessage(string action, Exception exception)
    {
        _ = exception;
        return FluxoLogManager.CreateFailureMessage(action);
    }

    private async Task SaveIsFirstRunAsync(bool isFirstRun)
    {
        await QuickSetupWizardShared.UpsertUserSettingAsync(_appData, UserSettingNames.IsFirstRun, isFirstRun.ToString());
        await _appData.SaveChangesAsync();
    }

    private async Task SyncRunAtStartupRegistrationAsync()
    {
        var setting = await _appData.GetUserSettingByNameAsync(UserSettingNames.ShouldRunAtStartup);
        var shouldRunAtStartup = UserSettingValueParser.ParseBool(setting?.Value, false);
        _startupRegistrationService.SetRunAtStartup(shouldRunAtStartup);
    }
}

