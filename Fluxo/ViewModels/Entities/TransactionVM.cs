using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Fluxo.Core.Enums;

namespace Fluxo.ViewModels.Entities;

public partial class TransactionVM : ObservableObject, IEquatable<TransactionVM>
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

    public ObservableCollection<TransactionVM> ChildTransactions { get; } = [];
    public decimal ChildAmountTotal => ChildTransactions.Sum(child => child.Amount);
    public decimal ChildAmountRemaining => Amount - ChildAmountTotal;
    public decimal ChildAmountOverflow => Math.Max(0m, ChildAmountTotal - Amount);
    public decimal ChildAmountDisplay => HasChildAmountOverflow ? ChildAmountOverflow : ChildAmountRemaining;
    public string ChildAmountStatus => HasChildAmountOverflow ? "overflowing" : "left";
    public bool HasChildAmountOverflow => ChildAmountTotal > Amount;
    public bool CanAddChildTransaction => Amount > 0m && !HasChildAmountOverflow;
    public bool IsLeaf => ChildTransactions.Count == 0;

    partial void OnAmountChanged(decimal value) => NotifyChildStateChanged();

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

    public bool Equals(TransactionVM? other)
    {
        if (ReferenceEquals(this, other))
            return true;

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

    public override bool Equals(object? obj) => Equals(obj as TransactionVM);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Type);
        hash.Add(SourceAccountId);
        hash.Add(GoalId);
        hash.Add(RepaymentAccountId);
        hash.Add(Name);
        hash.Add(Amount);
        hash.Add(OccurredOn);
        hash.Add(Notes);
        hash.Add(ExpenseCategory);
        hash.Add(Tag?.Id);
        hash.Add(IsPinned);
        hash.Add(IsIoU);
        hash.Add(ShouldAffectBalance);
        hash.Add(IsExcludedFromBudget);
        return hash.ToHashCode();
    }
}
