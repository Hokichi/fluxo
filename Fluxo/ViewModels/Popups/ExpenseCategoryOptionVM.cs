using CommunityToolkit.Mvvm.ComponentModel;
using Fluxo.Core.Enums;

namespace Fluxo.ViewModels.Popups;

public sealed partial class ExpenseCategoryOptionVM : ObservableObject
{
    public ExpenseCategoryOptionVM(string label, ExpenseCategory value)
    {
        Label = label;
        Value = value;
    }

    public string Label { get; }
    public ExpenseCategory Value { get; }

    [ObservableProperty] private bool _isEnabled = true;
}
