using Fluxo.DataModels.Popups.GlobalSearch;
using Fluxo.Helpers.MainWindow;
using Fluxo.Services.Notifications;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Popups.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Fluxo.Views.Shell.Main;

public partial class MainWindow
{
    internal async Task OpenGlobalSearchAsync()
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
                    await MainWindowFeatureActivationHelper.ActivateAsync(this, featureTarget);
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

    internal void OpenNewTagPopup()
    {
        using var scope = _serviceProvider.CreateScope();
        var tags = scope.ServiceProvider.GetRequiredService<SettingsTagsTabVM>();
        _dialogService.ShowAddTag(tags.CreateAddTagViewModel(), tags.CreateTagAsync, this);
    }
}
