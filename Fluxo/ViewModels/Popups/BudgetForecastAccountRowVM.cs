using CommunityToolkit.Mvvm.ComponentModel;

namespace Fluxo.ViewModels.Popups;

public sealed partial class BudgetForecastAccountRowVM(
    int id,
    string name,
    decimal currentBalance) : ObservableObject
{
    [ObservableProperty] private decimal _balance = currentBalance;

    public int Id { get; } = id;
    public string Name { get; } = name;
    public decimal CurrentBalance { get; } = currentBalance;
    public bool IsBalanceDecreased => Balance < CurrentBalance;
    public bool IsBalanceIncreased => Balance > CurrentBalance;

    partial void OnBalanceChanged(decimal value)
    {
        OnPropertyChanged(nameof(IsBalanceDecreased));
        OnPropertyChanged(nameof(IsBalanceIncreased));
    }
}
