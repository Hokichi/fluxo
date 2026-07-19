using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluxo.Core.Enums;
using Fluxo.ViewModels.Entities;
using System.Collections.ObjectModel;

namespace Fluxo.ViewModels.Popups;

public partial class TransactionSplitRowVM : ObservableObject
{
    public TransactionSplitRowVM()
    {
        ChildRows.CollectionChanged += (_, _) => RecalculateChildRemainder(ChildRows.LastOrDefault());
    }
    [ObservableProperty] private decimal _amountText;
    [ObservableProperty] private int? _transactionId;
    [ObservableProperty] private bool _isCausingNegativeRemainder;
    [ObservableProperty] private bool _hasNegativeChildRemainder;
    [ObservableProperty] private bool _isIoU;
    [ObservableProperty] private bool _isExcludedFromBudget;
    [ObservableProperty] private bool _isSplit;
    [ObservableProperty] private bool _isSplitEquallyEnabled;
    [ObservableProperty] private bool _isTagPopupOpen;
    [ObservableProperty] private decimal _remainingAmount;
    [ObservableProperty] private string _nameText = string.Empty;
    [ObservableProperty] private ExpenseCategory _selectedExpenseCategory = ExpenseCategory.Needs;
    [ObservableProperty] private TagVM? _selectedTag;

    public bool HasAmount => AmountText > 0m;
    public bool ShowLeafTags => !IsSplit;
    public bool CanSelectCategory => !IsSplit && !IsIoU;
    public bool IsNeedsCategory { get => SelectedExpenseCategory == ExpenseCategory.Needs; set { if (value) SelectedExpenseCategory = ExpenseCategory.Needs; } }
    public bool IsWantsCategory { get => SelectedExpenseCategory == ExpenseCategory.Wants; set { if (value) SelectedExpenseCategory = ExpenseCategory.Wants; } }
    public bool IsInvestCategory { get => SelectedExpenseCategory == ExpenseCategory.Savings; set { if (value) SelectedExpenseCategory = ExpenseCategory.Savings; } }
    public bool CanToggleRecurring => true;
    public bool CanUseIoU => true;
    public ObservableCollection<TransactionSplitRowVM> ChildRows { get; } = [];

    public bool IsRegularMode
    {
        get => !IsIoU;
        set { if (value) IsIoU = false; }
    }

    public bool IsPostedIoUMode
    {
        get => IsIoU;
        set { if (value) IsIoU = true; }
    }

    public bool HasMeaningfulValue =>
        AmountText > 0m ||
        IsIoU ||
        !string.IsNullOrWhiteSpace(NameText) ||
        SelectedTag is not null;

    public string AmountValidationHint =>
        IsCausingNegativeRemainder ? "Amount exceeds parent expense." : string.Empty;

    public string TagDisplayName => SelectedTag?.Name ?? "Tag";

    partial void OnAmountTextChanged(decimal value)
    {
        OnPropertyChanged(nameof(HasAmount));
        OnPropertyChanged(nameof(HasMeaningfulValue));

        if (IsSplitEquallyEnabled)
            ApplyEqualSplitAmounts();

        RecalculateChildRemainder(ChildRows.LastOrDefault());
    }

    partial void OnNameTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasMeaningfulValue));
    }

    partial void OnIsSplitChanged(bool value)
    {
        if (value && ChildRows.Count == 0)
            AddChildRow();

        OnPropertyChanged(nameof(HasMeaningfulValue));
        OnPropertyChanged(nameof(ShowLeafTags));
        OnPropertyChanged(nameof(CanSelectCategory));
        RecalculateChildRemainder(ChildRows.LastOrDefault());
    }

    partial void OnIsIoUChanged(bool value)
    {
        OnPropertyChanged(nameof(HasMeaningfulValue));
        OnPropertyChanged(nameof(IsRegularMode));
        OnPropertyChanged(nameof(IsPostedIoUMode));
        OnPropertyChanged(nameof(CanSelectCategory));
    }

    partial void OnSelectedTagChanged(TagVM? value)
    {
        OnPropertyChanged(nameof(HasMeaningfulValue));
        OnPropertyChanged(nameof(TagDisplayName));
        IsTagPopupOpen = false;
    }

    partial void OnIsCausingNegativeRemainderChanged(bool value)
    {
        OnPropertyChanged(nameof(AmountValidationHint));
    }

    partial void OnSelectedExpenseCategoryChanged(ExpenseCategory value)
    {
        OnPropertyChanged(nameof(IsNeedsCategory));
        OnPropertyChanged(nameof(IsWantsCategory));
        OnPropertyChanged(nameof(IsInvestCategory));
    }

    public void AddChildRow()
    {
        var child = new TransactionSplitRowVM
        {
            SelectedExpenseCategory = SelectedExpenseCategory,
            SelectedTag = SelectedTag,
            IsIoU = IsIoU,
            IsExcludedFromBudget = IsExcludedFromBudget
        };
        child.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AmountText))
                RecalculateChildRemainder(child);
        };
        ChildRows.Add(child);
    }

    public void RecalculateChildRemainder(TransactionSplitRowVM? changedChild)
    {
        var remainder = AmountText - ChildRows.Sum(child => child.AmountText);
        RemainingAmount = remainder;
        foreach (var child in ChildRows)
            child.IsCausingNegativeRemainder = false;

        HasNegativeChildRemainder = remainder < 0m;
        if (HasNegativeChildRemainder && changedChild is not null)
            changedChild.IsCausingNegativeRemainder = true;
    }

    [RelayCommand]
    private void SetSplitMode()
    {
        IsSplit = true;
        IsSplitEquallyEnabled = false;
        TransactionSplitVM.ClearSplitAmounts(ChildRows);
        RecalculateChildRemainder(ChildRows.LastOrDefault());
    }

    [RelayCommand]
    private void SetSplitEquallyMode()
    {
        IsSplit = true;
        IsSplitEquallyEnabled = true;
        ApplyEqualSplitAmounts();
    }

    private void ApplyEqualSplitAmounts()
    {
        TransactionSplitVM.ApplyEqualSplitAmounts(ChildRows, AmountText);
        RecalculateChildRemainder(ChildRows.LastOrDefault());
    }
}
