using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Enums;
using Fluxo.DataModels.Messages;
using Fluxo.DataModels.Popups.TransactionPopup;
using Fluxo.Helpers.Transactions;
using Fluxo.ViewModels.Entities;

namespace Fluxo.ViewModels.Popups;

public sealed partial class TransactionSplitsVM : ObservableObject, IDisposable
{
    private readonly IMessenger _messenger;
    private readonly HashSet<TransactionVM> _subscribedTransactions = [];
    private TransactionVM? _rootTransaction;
    private TransactionVM? _selectedSplitTransaction;
    private bool _isTabSelected;
    private bool _canModifySplitTree;
    private bool _isMutating;

    public TransactionSplitsVM(IMessenger messenger)
    {
        _messenger = messenger;
        messenger.Register<TransactionSplitsVM, TransactionSplitContextChangedMessage>(
            this, static (recipient, message) => recipient.ApplyContext(message));
        messenger.Register<TransactionSplitsVM, TransactionSplitValidationRequestedMessage>(
            this, static (_, message) => message.Reply(ValidateTree(message.Root)));
        messenger.Register<TransactionSplitsVM, TransactionSplitsClearRequestedMessage>(
            this, static (recipient, message) => recipient.ClearSplitTransactions(message.Value));
    }

    public TransactionVM? RootTransaction
    {
        get => _rootTransaction;
        private set => SetProperty(ref _rootTransaction, value);
    }

    public TransactionVM? SelectedSplitTransaction
    {
        get => _selectedSplitTransaction;
        private set => SetProperty(ref _selectedSplitTransaction, value);
    }

    public bool IsTabSelected
    {
        get => _isTabSelected;
        private set => SetProperty(ref _isTabSelected, value);
    }

    public bool CanModifySplitTree
    {
        get => _canModifySplitTree;
        private set => SetProperty(ref _canModifySplitTree, value);
    }

    public bool ShowInvalidSplitPlaceholder =>
        RootTransaction is not null &&
        (RootTransaction.Type != TransactionType.Expense || !RootTransaction.IsValid);
    public decimal SplitAmountRemaining => RootTransaction is null
        ? 0m
        : RootTransaction.HasChildAmountOverflow
            ? RootTransaction.ChildAmountOverflow
            : TransactionSplitHelper.GetRemainingAmount(RootTransaction);
    public string SplitAmountStatus => HasSplitAmountOverflow ? "overflowing" : "left";
    public bool HasSplitAmountOverflow =>
        RootTransaction is not null && TransactionSplitHelper.HasOverflow(RootTransaction);
    public bool HasSplitTransactions => RootTransaction?.ChildTransactions.Count > 0;
    public bool IsSplitRootOrChild => HasSplitTransactions &&
                                      (SelectedSplitTransaction is null ||
                                       SelectedSplitTransaction.ChildTransactions.Count > 0);
    public bool ShowRootSplitAddAction => CanModifySplitTree && !HasSplitTransactions;
    public bool ShowRootSplitPlusAction => CanModifySplitTree && HasSplitTransactions;

    [RelayCommand(CanExecute = nameof(CanAddSplit))]
    private void AddSplit(TransactionVM? parent)
    {
        if (RootTransaction is null || !CanAddSplit(parent))
            return;

        Mutate(() =>
        {
            var child = TransactionSplitHelper.AddChild(RootTransaction, parent);
            NormalizeClassifications();
            SelectTransaction(child);
        });
    }

    private bool CanAddSplit(TransactionVM? parent) =>
        RootTransaction is not null && CanModifySplitTree &&
        TransactionSplitHelper.CanAddChild(RootTransaction, parent) &&
        TransactionSplitHelper.IsValidParent(parent ?? RootTransaction);

    [RelayCommand(CanExecute = nameof(CanModifySplitTree))]
    private void DeleteSplit(TransactionVM node)
    {
        if (RootTransaction is null)
            return;

        Mutate(() =>
        {
            if (!TransactionSplitHelper.Remove(RootTransaction, node, out var parent))
                return;
            SubscribeTree();
            NormalizeClassifications();
            SelectTransaction(parent);
        });
    }

    [RelayCommand]
    private void SelectSplit(TransactionVM? node) => SelectTransaction(node);

    [RelayCommand(CanExecute = nameof(CanSplitEqually))]
    private void SplitEqually(TransactionVM? parent)
    {
        if (RootTransaction is null)
            return;
        Mutate(() => TransactionSplitHelper.SplitEqually(parent ?? RootTransaction));
    }

    private bool CanSplitEqually(TransactionVM? parent) =>
        RootTransaction is not null && CanModifySplitTree &&
        (parent ?? RootTransaction).ChildTransactions.Count >= 2;

    [RelayCommand(CanExecute = nameof(CanResetSplit))]
    private void ResetSplit(TransactionVM? parent)
    {
        if (RootTransaction is null)
            return;
        Mutate(() => TransactionSplitHelper.Reset(parent ?? RootTransaction));
    }

    private bool CanResetSplit(TransactionVM? parent) =>
        RootTransaction is not null && CanModifySplitTree &&
        (parent ?? RootTransaction).ChildTransactions.Count > 0;

    public void ClearSplitTransactions()
    {
        if (RootTransaction is not null)
            ClearSplitTransactions(RootTransaction);
    }

    private void ClearSplitTransactions(TransactionVM root)
    {
        if (!ReferenceEquals(root, RootTransaction) || root.ChildTransactions.Count == 0)
            return;

        Mutate(() =>
        {
            root.ChildTransactions.Clear();
            SubscribeTree();
            SelectTransaction(null);
        });
    }

    private void ApplyContext(TransactionSplitContextChangedMessage message)
    {
        var rootChanged = !ReferenceEquals(RootTransaction, message.Root);
        IsTabSelected = message.IsTabSelected;
        CanModifySplitTree = message.CanModify;

        if (rootChanged)
        {
            UnsubscribeTree();
            RootTransaction = message.Root;
            SelectedSplitTransaction = null;
            SubscribeTree();
            _messenger.Send(new TransactionLoadRequestedMessage(message.Root));
        }
        else if (!message.IsTabSelected && SelectedSplitTransaction is not null)
        {
            SelectTransaction(null);
        }

        NotifyStateChanged();
    }

    private void SelectTransaction(TransactionVM? node)
    {
        if (RootTransaction is null || ReferenceEquals(SelectedSplitTransaction, node))
            return;

        SelectedSplitTransaction = node;
        _messenger.Send(new TransactionLoadRequestedMessage(node ?? RootTransaction));
        NotifyStateChanged();
    }

    private void Mutate(Action action)
    {
        _isMutating = true;
        try
        {
            action();
        }
        finally
        {
            _isMutating = false;
        }

        SubscribeTree();
        PublishChange();
    }

    private void NormalizeClassifications()
    {
        if (RootTransaction is null || RootTransaction.ChildTransactions.Count == 0)
            return;

        ClearClassification(RootTransaction);
        foreach (var child in RootTransaction.ChildTransactions)
            if (child.ChildTransactions.Count > 0)
                ClearClassification(child);
    }

    private static void ClearClassification(TransactionVM transaction)
    {
        transaction.Tag = null;
        transaction.ExpenseCategory = null;
    }

    private static TransactionPopupSubmissionResult ValidateTree(TransactionVM root)
    {
        if (TransactionSplitHelper.HasOverflow(root))
            return TransactionPopupSubmissionResult.Failure(
                "Split amounts cannot exceed their parent amount.");
        if (!TransactionSplitHelper.IsBalanced(root))
            return TransactionPopupSubmissionResult.Failure(
                "Split amounts must equal their parent amount.");
        if (!ValidateNodes(root, 0))
            return TransactionPopupSubmissionResult.Failure(
                "Please fix the invalid sub-transactions.");
        return TransactionPopupSubmissionResult.Success();
    }

    private static bool ValidateNodes(TransactionVM parent, int depth)
    {
        if (depth > 2)
            return false;

        foreach (var child in parent.ChildTransactions)
        {
            if (string.IsNullOrWhiteSpace(child.Name) || child.Amount <= 0m ||
                child.IsLeaf && child.Type == TransactionType.Expense && child.Tag is null ||
                !ValidateNodes(child, depth + 1))
                return false;
        }

        return true;
    }

    private void SubscribeTree()
    {
        UnsubscribeTree();
        if (RootTransaction is not null)
            SubscribeNode(RootTransaction);
    }

    private void SubscribeNode(TransactionVM transaction)
    {
        if (!_subscribedTransactions.Add(transaction))
            return;

        transaction.PropertyChanged += OnTransactionPropertyChanged;
        transaction.ChildTransactions.CollectionChanged += OnChildTransactionsChanged;
        foreach (var child in transaction.ChildTransactions)
            SubscribeNode(child);
    }

    private void UnsubscribeTree()
    {
        foreach (var transaction in _subscribedTransactions)
        {
            transaction.PropertyChanged -= OnTransactionPropertyChanged;
            transaction.ChildTransactions.CollectionChanged -= OnChildTransactionsChanged;
        }
        _subscribedTransactions.Clear();
    }

    private void OnTransactionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_isMutating)
            PublishChange();
    }

    private void OnChildTransactionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isMutating)
            return;
        SubscribeTree();
        NormalizeClassifications();
        PublishChange();
    }

    private void PublishChange()
    {
        if (RootTransaction is not null)
            _messenger.Send(new TransactionSplitChangedMessage(RootTransaction));
        NotifyStateChanged();
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(ShowInvalidSplitPlaceholder));
        OnPropertyChanged(nameof(SplitAmountRemaining));
        OnPropertyChanged(nameof(SplitAmountStatus));
        OnPropertyChanged(nameof(HasSplitAmountOverflow));
        OnPropertyChanged(nameof(HasSplitTransactions));
        OnPropertyChanged(nameof(IsSplitRootOrChild));
        OnPropertyChanged(nameof(ShowRootSplitAddAction));
        OnPropertyChanged(nameof(ShowRootSplitPlusAction));
        AddSplitCommand.NotifyCanExecuteChanged();
        DeleteSplitCommand.NotifyCanExecuteChanged();
        SplitEquallyCommand.NotifyCanExecuteChanged();
        ResetSplitCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        UnsubscribeTree();
        _messenger.UnregisterAll(this);
    }
}
