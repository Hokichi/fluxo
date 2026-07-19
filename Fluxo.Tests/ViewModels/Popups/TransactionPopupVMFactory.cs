using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Interfaces.Services;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Popups;
using Fluxo.ViewModels.Shell.Main;

namespace Fluxo.Tests.ViewModels.Popups;

internal static class TransactionPopupVMFactory
{
    internal static TransactionPopupVM Create(
        MainVM mainViewModel,
        IAppDataService appData,
        IReadOnlyList<AccountVM>? accountsOverride = null,
        Func<TransactionPopupVM.RecurringDraftSaveInput,
            Task<TransactionPopupVM.TransactionPopupSubmissionResult>>? saveRecurringDraftAsync = null,
        IMessenger? messenger = null)
    {
        var viewModel = new TransactionPopupVM(appData, messenger ?? new WeakReferenceMessenger());
        viewModel.ConfigureCatalogs(
            accountsOverride ?? mainViewModel.BudgetPanel.Accounts,
            mainViewModel.BudgetPanel.Tags.Concat(mainViewModel.BudgetPanel.OtherTags).ToArray(),
            mainViewModel.SavingGoalsPanel.SavingGoals);
        if (saveRecurringDraftAsync is not null)
            viewModel.ConfigureRecurringDraftSave(saveRecurringDraftAsync);
        return viewModel;
    }
}
