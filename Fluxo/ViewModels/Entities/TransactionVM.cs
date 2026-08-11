using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Fluxo.Core.Enums;
using Fluxo.DataModels.Popups.TransactionPopup;
using Fluxo.Helpers.Transaction;

namespace Fluxo.ViewModels.Entities;

public partial class TransactionVM : ObservableObject
{
    private readonly HashSet<TransactionVM> _subscribedChildren = [];

    public TransactionVM()
    {
        ChildTransactions.CollectionChanged += OnChildTransactionsCollectionChanged;
    }

    [ObservableProperty] private int _id;
    [ObservableProperty] private TransactionType _type;
    [ObservableProperty] private int _sourceAccountId;
    [ObservableProperty] private int? _goalId;
    [ObservableProperty] private int? _repaymentAccountId;
    [ObservableProperty] private AccountVM _account = new();
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private decimal _amount;
    [ObservableProperty] private DateTime _occurredOn;
    [ObservableProperty] private DateTime _loggedOn;
    [ObservableProperty] private string _notes = string.Empty;
    [ObservableProperty] private ExpenseCategory? _expenseCategory;
    [ObservableProperty] private TagVM? _tag;
    [ObservableProperty] private int? _parentTransactionId;
    [ObservableProperty] private bool _isPinned;
    [ObservableProperty] private bool _isForDeletion;
    [ObservableProperty] private bool _isIoU;
    [ObservableProperty] private bool _shouldAffectBalance;
    [ObservableProperty] private bool _isExcludedFromBudget;
    [ObservableProperty] private bool _isValid;

    public ObservableCollection<TransactionVM> ChildTransactions { get; } = [];
    public DateTime OccurredOnDate => OccurredOn.Date;
    public TimeSpan OccurredOnTime => OccurredOn.TimeOfDay;
    public decimal ChildAmountTotal => ChildTransactions.Sum(child => child.Amount);
    public decimal ChildAmountRemaining => Amount - ChildAmountTotal;
    public decimal ChildAmountOverflow => Math.Max(0m, ChildAmountTotal - Amount);
    public decimal ChildAmountDisplay => HasChildAmountOverflow ? ChildAmountOverflow : ChildAmountRemaining;
    public string ChildAmountStatus => HasChildAmountOverflow ? "overflowing" : "left";
    public bool HasChildAmountOverflow => ChildAmountTotal > Amount;
    public bool CanAddChildTransaction => Amount > 0m && !HasChildAmountOverflow;
    public bool IsLeaf => ChildTransactions.Count == 0;

    public void Validate(TransactionValidationContext context)
    {
        var amount = Amount;
        if (context.IsInstallments)
        {
            var installments = RecurringTransactionValidationHelper.ValidateInstallments(
                context.RecurringPeriod, context.RecurringTimeText,
                context.InstallmentEndDate, context.StartDate);
            if (!installments.IsValid)
            {
                IsValid = false;
                return;
            }

            amount = TransactionCalculationHelper.CalculateInstallmentAmount(amount, installments.OccurrenceCount);
        }

        IsValid = context.IsSplitTreeValid &&
                  TransactionValidationHelper.ValidateName(Name, context.IsGoal).IsValid &&
                  SourceAccountId > 0 &&
                  (!context.IsGoal || GoalId is > 0) &&
                  (!context.IsRepayment || RepaymentAccountId is > 0) &&
                  (!context.IsRecurring || RecurringTransactionValidationHelper
                      .ValidateTime(context.RecurringPeriod, context.RecurringTimeText).IsValid) &&
                  TransactionValidationHelper.ValidateAmount(
                      amount, context.IsRepaymentAmountInvalid, Type == TransactionType.Expense,
                      context.IsGoal, context.AmountValidationAccount, context.IgnoreMaximumSpending).IsValid &&
                  TransactionValidationHelper.ValidateTagSpending(
                      Type == TransactionType.Expense, context.IsRecurring || context.IsInstallments,
                      IsExcludedFromBudget, Tag, context.CurrentTagSpending, amount).IsValid;
    }

    partial void OnAmountChanged(decimal value) => NotifyChildStateChanged();

    partial void OnOccurredOnChanged(DateTime value)
    {
        OnPropertyChanged(nameof(OccurredOnDate));
        OnPropertyChanged(nameof(OccurredOnTime));
    }

    private void OnChildTransactionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (TransactionVM child in e.OldItems)
                UnsubscribeChild(child);

        if (e.NewItems is not null)
            foreach (TransactionVM child in e.NewItems)
                SubscribeChild(child);

        if (e.Action == NotifyCollectionChangedAction.Reset)
            foreach (var child in _subscribedChildren.ToList())
                UnsubscribeChild(child);

        NotifyChildStateChanged();
    }

    private void SubscribeChild(TransactionVM child)
    {
        if (_subscribedChildren.Add(child))
            child.PropertyChanged += OnChildPropertyChanged;
    }

    private void UnsubscribeChild(TransactionVM child)
    {
        if (_subscribedChildren.Remove(child))
            child.PropertyChanged -= OnChildPropertyChanged;
    }

    private void OnChildPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Amount) or null)
            NotifyChildStateChanged();
    }

    private void NotifyChildStateChanged()
    {
        OnPropertyChanged(nameof(ChildAmountTotal));
        OnPropertyChanged(nameof(ChildAmountRemaining));
        OnPropertyChanged(nameof(ChildAmountOverflow));
        OnPropertyChanged(nameof(ChildAmountDisplay));
        OnPropertyChanged(nameof(ChildAmountStatus));
        OnPropertyChanged(nameof(HasChildAmountOverflow));
        OnPropertyChanged(nameof(CanAddChildTransaction));
        OnPropertyChanged(nameof(IsLeaf));
    }

    public bool HasSameValues(TransactionVM? other)
    {
        return other is not null &&
            Type == other.Type &&
            SourceAccountId == other.SourceAccountId &&
            GoalId == other.GoalId &&
            RepaymentAccountId == other.RepaymentAccountId &&
            Name == other.Name &&
            Amount == other.Amount &&
            OccurredOn == other.OccurredOn &&
            Notes == other.Notes &&
            ExpenseCategory == other.ExpenseCategory &&
            Tag?.Id == other.Tag?.Id &&
            IsPinned == other.IsPinned &&
            IsIoU == other.IsIoU &&
            ShouldAffectBalance == other.ShouldAffectBalance &&
            IsExcludedFromBudget == other.IsExcludedFromBudget;
    }

}
