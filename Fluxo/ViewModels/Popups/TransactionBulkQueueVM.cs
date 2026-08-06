using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Enums;
using Fluxo.DataModels.Messages;
using Fluxo.ViewModels.Entities;

namespace Fluxo.ViewModels.Popups;

public sealed partial class TransactionBulkQueueVM : ObservableObject, IDisposable
{
    private readonly IMessenger _messenger;
    private AccountVM? _defaultAccount;
    private TransactionVM? _selectedQueuedTransaction;

    public TransactionBulkQueueVM(IMessenger messenger)
    {
        _messenger = messenger;
        messenger.Register<TransactionBulkQueueVM, TransactionBulkQueueResetMessage>(
            this, static (recipient, message) => recipient.Reset(message));
        messenger.Register<TransactionBulkQueueVM, TransactionBulkQueueSelectRequestedMessage>(
            this, static (recipient, message) =>
                recipient.SelectedQueuedTransaction = message.Value);
        messenger.Register<TransactionBulkQueueVM, TransactionBulkQueueRemoveRequestedMessage>(
            this, static (recipient, message) => recipient.Remove(message.Value));
    }

    public ObservableCollection<TransactionVM> QueuedTransactions { get; } = [];

    public bool IsBulkMode { get; private set; }

    public TransactionVM? SelectedQueuedTransaction
    {
        get => _selectedQueuedTransaction;
        set
        {
            if (!SetProperty(ref _selectedQueuedTransaction, value))
                return;

            PublishState();
            if (value is not null)
                _messenger.Send(new TransactionLoadRequestedMessage(value));
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
            OccurredOn = DateTime.Today
        };
        Add(transaction);
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
        var index = QueuedTransactions.IndexOf(transaction);
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
        if (e.PropertyName is nameof(TransactionVM.Name) or nameof(TransactionVM.Amount)
            or nameof(TransactionVM.IsValid))
            PublishState();
    }

    private void PublishState() =>
        _messenger.Send(new TransactionBulkQueueStateChangedMessage(
            QueuedTransactions,
            SelectedQueuedTransaction,
            QueuedTransactions.Any(transaction =>
                !string.IsNullOrEmpty(transaction.Name) || transaction.Amount != 0m),
            QueuedTransactions.All(transaction => transaction.IsValid)));

    public void Dispose()
    {
        foreach (var transaction in QueuedTransactions)
            transaction.PropertyChanged -= OnTransactionPropertyChanged;
        _messenger.UnregisterAll(this);
    }
}
