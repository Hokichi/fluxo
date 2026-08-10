using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Enums;
using Fluxo.Converters;
using Fluxo.DataModels.Messages;
using Fluxo.ViewModels.Entities;
using System.ComponentModel;
using System.Windows.Data;

namespace Fluxo.ViewModels.Popups;

public sealed partial class TransactionBulkQueueVM : ObservableObject, IDisposable
{
    private readonly IMessenger _messenger;
    private readonly TransactionPopupMessageToken _messageToken;
    private AccountVM? _defaultAccount;
    private TransactionVM? _selectedQueuedTransaction;

    public TransactionBulkQueueVM(IMessenger messenger)
        : this(messenger, TransactionPopupMessageToken.Default)
    {
    }

    public TransactionBulkQueueVM(IMessenger messenger, TransactionPopupMessageToken messageToken)
    {
        _messenger = messenger;
        _messageToken = messageToken;
        messenger.Register<TransactionBulkQueueVM, TransactionBulkQueueResetMessage, TransactionPopupMessageToken>(
            this, messageToken, static (recipient, message) => recipient.Reset(message));
        messenger.Register<TransactionBulkQueueVM, TransactionBulkQueueSelectRequestedMessage, TransactionPopupMessageToken>(
            this, messageToken, static (recipient, message) =>
                recipient.SelectedQueuedTransaction = message.Value);
        messenger.Register<TransactionBulkQueueVM, TransactionBulkQueueRemoveRequestedMessage, TransactionPopupMessageToken>(
            this, messageToken, static (recipient, message) => recipient.Remove(message.Value));

        QueuedTransactionsView = CollectionViewSource.GetDefaultView(QueuedTransactions);
        QueuedTransactionsView.GroupDescriptions.Add(
            new PropertyGroupDescription(nameof(TransactionVM.OccurredOn), new TransactionDateGroupConverter()));
        QueuedTransactionsView.SortDescriptions.Add(
            new SortDescription(nameof(TransactionVM.OccurredOn), ListSortDirection.Descending));
    }

    public ObservableCollection<TransactionVM> QueuedTransactions { get; } = [];
    public ICollectionView QueuedTransactionsView { get; }

    public bool IsBulkMode { get; private set; }

    public TransactionVM? SelectedQueuedTransaction
    {
        get => _selectedQueuedTransaction;
        set
        {
            if (ReferenceEquals(_selectedQueuedTransaction, value))
                return;

            OnPropertyChanging();
            _selectedQueuedTransaction = value;
            OnPropertyChanged();
            PublishState();
            if (value is not null)
                _messenger.Send(new TransactionLoadRequestedMessage(value), _messageToken);
        }
    }

    [RelayCommand]
    private void AddQueuedTransaction()
    {
        if (!IsBulkMode)
            return;

        var transaction = new TransactionVM
        {
            Type = TransactionType.Expense,
            Account = _defaultAccount ?? new AccountVM(),
            SourceAccountId = _defaultAccount?.Id ?? 0,
            OccurredOn = DateTime.Now
        };
        Add(transaction);
        SelectedQueuedTransaction = transaction;
    }

    [RelayCommand]
    private void ActivateQueuedTransaction(TransactionVM? transaction)
    {
        if (transaction is null)
            return;

        if (ReferenceEquals(SelectedQueuedTransaction, transaction))
            _messenger.Send(new TransactionLoadRequestedMessage(transaction), _messageToken);
        else
            SelectedQueuedTransaction = transaction;
    }

    private void Reset(TransactionBulkQueueResetMessage message)
    {
        foreach (var transaction in QueuedTransactions)
            transaction.PropertyChanged -= OnTransactionPropertyChanged;

        QueuedTransactions.Clear();
        IsBulkMode = message.IsEnabled;
        _defaultAccount = message.DefaultAccount;

        foreach (var transaction in message.Transactions)
            Add(transaction);

        SelectedQueuedTransaction = QueuedTransactions.FirstOrDefault();
        PublishState();
    }

    private void Add(TransactionVM transaction)
    {
        QueuedTransactions.Add(transaction);
        transaction.PropertyChanged += OnTransactionPropertyChanged;
        PublishState();
    }

    private void Remove(TransactionVM transaction)
    {
        var index = QueuedTransactions.ToList().FindIndex(candidate => ReferenceEquals(candidate, transaction));
        if (index < 0)
            return;

        transaction.PropertyChanged -= OnTransactionPropertyChanged;
        QueuedTransactions.RemoveAt(index);
        if (ReferenceEquals(SelectedQueuedTransaction, transaction))
            SelectedQueuedTransaction = QueuedTransactions.ElementAtOrDefault(
                Math.Min(index, QueuedTransactions.Count - 1));
        else
            PublishState();
    }

    private void OnTransactionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TransactionVM.OccurredOn))
            QueuedTransactionsView.Refresh();

        if (e.PropertyName is nameof(TransactionVM.Name) or nameof(TransactionVM.Amount)
            or nameof(TransactionVM.OccurredOn) or nameof(TransactionVM.IsValid))
            PublishState();
    }

    private void PublishState() =>
        _messenger.Send(new TransactionBulkQueueStateChangedMessage(
            QueuedTransactions,
            SelectedQueuedTransaction,
            QueuedTransactions.Any(transaction =>
                !string.IsNullOrEmpty(transaction.Name) || transaction.Amount != 0m),
            QueuedTransactions.All(transaction => transaction.IsValid)), _messageToken);

    public void Dispose()
    {
        foreach (var transaction in QueuedTransactions)
            transaction.PropertyChanged -= OnTransactionPropertyChanged;
        _messenger.UnregisterAll(this);
    }
}
