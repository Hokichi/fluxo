using CommunityToolkit.Mvvm.Messaging;
using System.Runtime.CompilerServices;
using Fluxo.Core.Interfaces.Services;
using Fluxo.DataModels.Messages;
using Fluxo.Tests.Helpers.Popups;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Popups;
using Fluxo.ViewModels.Shell.Main;

namespace Fluxo.Tests.ViewModels.Popups;

internal static class TransactionPopupVMFactory
{
    private static readonly ConditionalWeakTable<TransactionPopupVM, TransactionPopupPeers> Peers = new();

    internal static TransactionPopupVM Create(
        MainVM mainViewModel,
        IAppDataService appData,
        IReadOnlyList<AccountVM>? accountsOverride = null,
        Func<RecurringDraftSaveInput,
            Task<TransactionPopupSubmissionResult>>? saveRecurringDraftAsync = null,
        IMessenger? messenger = null)
    {
        return CreatePeers(mainViewModel, appData, accountsOverride, saveRecurringDraftAsync, messenger).Popup;
    }

    internal static TransactionPopupPeers CreatePeers(
        MainVM mainViewModel,
        IAppDataService appData,
        IReadOnlyList<AccountVM>? accountsOverride = null,
        Func<RecurringDraftSaveInput,
            Task<TransactionPopupSubmissionResult>>? saveRecurringDraftAsync = null,
        IMessenger? messenger = null)
    {
        messenger ??= new WeakReferenceMessenger();
        var messageToken = new TransactionPopupMessageToken(Guid.NewGuid());
        var viewModel = new TransactionPopupVM(appData, messenger, messageToken);
        var bulk = new TransactionBulkQueueVM(messenger, messageToken);
        var splits = new TransactionSplitsVM(messenger, messageToken);
        viewModel.ConfigureCatalogs(
            accountsOverride ?? mainViewModel.BudgetPanel.Accounts,
            mainViewModel.BudgetPanel.Tags.Concat(mainViewModel.BudgetPanel.OtherTags).ToArray(),
            mainViewModel.SavingGoalsPanel.SavingGoals);
        if (saveRecurringDraftAsync is not null)
            viewModel.ConfigureRecurringDraftSave(saveRecurringDraftAsync);
        var peers = new TransactionPopupPeers(viewModel, bulk, splits);
        Peers.Add(viewModel, peers);
        return peers;
    }
}
