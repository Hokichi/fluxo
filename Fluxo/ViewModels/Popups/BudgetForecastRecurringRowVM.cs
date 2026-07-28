using CommunityToolkit.Mvvm.ComponentModel;
using Fluxo.Core.Enums;

namespace Fluxo.ViewModels.Popups;

public sealed partial class BudgetForecastRecurringRowVM : ObservableObject
{
    [ObservableProperty] private bool _isIncluded;

    public BudgetForecastRecurringRowVM(
        int id,
        string name,
        decimal amount,
        int accountId,
        string accountName,
        RecurringTransactionType type,
        ExpenseCategory category,
        RecurringPeriod recurringPeriod,
        int recurringTime,
        bool isIncluded)
    {
        Id = id;
        Name = name;
        Amount = amount;
        AccountId = accountId;
        AccountName = accountName;
        Type = type;
        Category = category;
        RecurringPeriod = recurringPeriod;
        RecurringTime = recurringTime;
        _isIncluded = isIncluded;
    }

    public int Id { get; }
    public string Name { get; }
    public decimal Amount { get; }
    public decimal SignedAmount => Type == RecurringTransactionType.Income ? Amount : -Amount;
    public int AccountId { get; }
    public string AccountName { get; }
    public RecurringTransactionType Type { get; }
    public ExpenseCategory Category { get; }
    public RecurringPeriod RecurringPeriod { get; }
    public int RecurringTime { get; }
}
