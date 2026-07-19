using CommunityToolkit.Mvvm.ComponentModel;
using Fluxo.Core.Enums;

namespace Fluxo.ViewModels.Entities;

public partial class TransactionVM : ObservableObject, IEquatable<TransactionVM>
{
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
