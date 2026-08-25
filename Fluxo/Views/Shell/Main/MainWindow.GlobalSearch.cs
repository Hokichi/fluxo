using Fluxo.DataModels.Popups.GlobalSearch;
using Fluxo.Helpers.MainWindow;
using Fluxo.Services.Notifications;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Popups.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Fluxo.Views.Shell.Main;

public partial class MainWindow
{
    private async Task OpenGlobalSearchAsync()
    {
        var candidateTask = new GlobalSearchCandidateLoader(_appData).LoadAsync();
        var result = _dialogService.ShowGlobalSearch(candidateTask, this);
        if (result is not null)
            await ActivateGlobalSearchResultAsync(result);
    }

    private async Task ActivateGlobalSearchResultAsync(GlobalSearchResult result)
    {
        try
        {
            switch (result.Type)
            {
                case GlobalSearchResultType.Transactions when result.EntityId is int transactionId:
                    OpenLedgerTransactionDetailPopup(transactionId);
                    return;

                case GlobalSearchResultType.Accounts when result.EntityId is int accountId:
                    await OpenGlobalSearchAccountAsync(accountId);
                    return;

                case GlobalSearchResultType.Tags when result.EntityId is int tagId:
                    await OpenGlobalSearchTagAsync(tagId);
                    return;

                case GlobalSearchResultType.Goals when result.EntityId is int goalId:
                    OpenEditSavingGoalPopup(goalId);
                    return;

                case GlobalSearchResultType.Features when result.FeatureTarget is GlobalSearchFeatureTarget featureTarget:
                    await ActivateGlobalSearchFeatureAsync(featureTarget);
                    return;

                case GlobalSearchResultType.Settings when result.SettingsTarget is not null:
                    _dialogService.ShowSettings(result.SettingsTarget, this);
                    return;
            }
        }
        catch (Exception exception)
        {
            FloatingNotificationPublisher.LoggedFailure(_messenger, exception, "open search result");
        }
    }

    private async Task OpenGlobalSearchAccountAsync(int accountId)
    {
        if (await _appData.GetAccountByIdAsync(accountId) is null)
            return;

        OpenAccountDetailPopup(new AccountVM { Id = accountId });
    }

    private async Task OpenGlobalSearchTagAsync(int tagId)
    {
        using var scope = _serviceProvider.CreateScope();
        var tags = scope.ServiceProvider.GetRequiredService<SettingsTagsTabVM>();
        var viewModel = await tags.CreateEditTagViewModelAsync(tagId);
        if (viewModel is not null)
            _dialogService.ShowAddTag(viewModel, (name, hexCode, spendingLimit) =>
                tags.UpdateTagAsync(tagId, name, hexCode, spendingLimit), this);
    }

    private async Task ActivateGlobalSearchFeatureAsync(GlobalSearchFeatureTarget target)
    {
        switch (target)
        {
            case GlobalSearchFeatureTarget.Dashboard:
                await NavigateToMainPageAsync(MainPage.Dashboard);
                break;
            case GlobalSearchFeatureTarget.Analytics:
                await NavigateToMainPageAsync(MainPage.Analytics);
                break;
            case GlobalSearchFeatureTarget.Calendar:
                await NavigateToMainPageAsync(MainPage.Calendar);
                break;
            case GlobalSearchFeatureTarget.Ledger:
                await NavigateToMainPageAsync(MainPage.Ledger);
                break;
            case GlobalSearchFeatureTarget.QuickAccess:
                OpenQuickAddPopup();
                break;
            case GlobalSearchFeatureTarget.NewAccount:
                OpenAddAccountPopup();
                break;
            case GlobalSearchFeatureTarget.NewTransaction:
                OpenAddNewTransactionPopup();
                break;
            case GlobalSearchFeatureTarget.NewTag:
                OpenGlobalSearchNewTag();
                break;
            case GlobalSearchFeatureTarget.NewSavingGoal:
                OpenAddSavingGoalPopup();
                break;
            case GlobalSearchFeatureTarget.ViewAccounts:
                OpenAccountsListPopup();
                break;
            case GlobalSearchFeatureTarget.OpenSettings:
                OpenSettingsPopup();
                break;
            case GlobalSearchFeatureTarget.RunQuickSetup:
                OpenQuickSetupWizardPopup();
                break;
            case GlobalSearchFeatureTarget.CheckForUpdates:
                await CheckForUpdatesFromQuickAccessAsync();
                break;
            case GlobalSearchFeatureTarget.PlanningReport:
                OpenPlanningReport();
                break;
            case GlobalSearchFeatureTarget.BudgetForecast:
                OpenBudgetForecast();
                break;
            case GlobalSearchFeatureTarget.DataManagement:
                OpenDataManagementPopup();
                break;
            case GlobalSearchFeatureTarget.Hotkeys:
                OpenHotkeysOverviewPopup();
                break;
            case GlobalSearchFeatureTarget.LockApplication:
                LockAppUiFromUser();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target));
        }
    }

    private void OpenGlobalSearchNewTag()
    {
        using var scope = _serviceProvider.CreateScope();
        var tags = scope.ServiceProvider.GetRequiredService<SettingsTagsTabVM>();
        _dialogService.ShowAddTag(tags.CreateAddTagViewModel(), tags.CreateTagAsync, this);
    }
}
