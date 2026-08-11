using CommunityToolkit.Mvvm.ComponentModel;
using Fluxo.Core.Enums;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Fluxo.ViewModels.Shell.Main;

public sealed partial class LedgerTransactionItemVM : ObservableObject
{
    [ObservableProperty] private decimal _amount;
    [ObservableProperty] private int _accountId;
    [ObservableProperty] private int _tagId;
    [ObservableProperty] private bool _isChildrenExpanded;
    [ObservableProperty] private bool _isLastVisibleInGroup;
    [ObservableProperty] private bool _isSelectedForBatch;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _accountName = string.Empty;
    [ObservableProperty] private string _tagHexCode = string.Empty;
    [ObservableProperty] private string _tagName = string.Empty;

    public int Id { get; init; }
    public LedgerTransactionKind Kind { get; init; }
    public DateTime OccurredOn { get; init; }
    public DateTime LoggedOn { get; init; }
    public ExpenseCategory? Category { get; init; }
    public int? ParentTransactionId { get; init; }
    public bool IsChildTransaction { get; init; }
    public bool IsGoal { get; init; }
    public bool IsRecurring { get; init; }
    public ObservableCollection<LedgerTransactionItemVM> ChildTransactions { get; } = [];
    public ObservableCollection<LedgerTransactionItemVM> VisibleChildTransactions { get; } = [];
    public bool HasChildTransactions => ChildTransactions.Count > 0;
    public int VisibleChildCount => VisibleChildTransactions.Count;

    public decimal SignedAmount => Kind == LedgerTransactionKind.Income ? Amount : -Amount;
    public string TypeLabel => Kind == LedgerTransactionKind.Income ? "Incomes" : "Expenses";
    public string CategoryLabel => Category switch
    {
        ExpenseCategory.Needs => "Needs",
        ExpenseCategory.Wants => "Wants",
        ExpenseCategory.Savings => "Invest",
        ExpenseCategory.Excluded => "Excluded",
        _ => "Uncategorized"
    };

    public string DateGroupKey => OccurredOn.ToString("MMM dd", CultureInfo.InvariantCulture).ToUpperInvariant();
    public string TagGroupKey => string.IsNullOrWhiteSpace(TagName) ? "Untagged" : TagName;
    public string AccountGroupKey => string.IsNullOrWhiteSpace(AccountName) ? "No source" : AccountName;
    public string TypeGroupKey => TypeLabel;
    public string CategoryGroupKey => CategoryLabel;

    partial void OnAmountChanged(decimal value)
    {
        OnPropertyChanged(nameof(SignedAmount));
    }

    partial void OnIsSelectedForBatchChanged(bool value)
    {
        foreach (var child in ChildTransactions)
            child.IsSelectedForBatch = value;
    }

    public void RefreshChildTransactionState()
    {
        OnPropertyChanged(nameof(HasChildTransactions));
    }

    public void SetVisibleChildTransactions(IEnumerable<LedgerTransactionItemVM> children)
    {
        VisibleChildTransactions.Clear();
        foreach (var child in children)
            VisibleChildTransactions.Add(child);

        OnPropertyChanged(nameof(VisibleChildCount));
    }
}
