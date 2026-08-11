using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Enums;
using Fluxo.Resources.Resources.Messages;

namespace Fluxo.ViewModels.Entities;

public partial class RecurringTransactionVM : ObservableRecipient, IRecipient<StartupNotificationStateChangedMessage>
{
    public RecurringTransactionVM() : base(WeakReferenceMessenger.Default)
    {
        IsActive = true;
    }

    public void Receive(StartupNotificationStateChangedMessage message) =>
        IsOverdue = message.Value.OverdueRecurringTransactionIds.Contains(Id);

    [ObservableProperty] private int _id;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private decimal _amount;
    [ObservableProperty] private RecurringPeriod _recurringPeriod;
    [ObservableProperty] private int _recurringTime;
    [ObservableProperty] private RecurringTransactionType _type;
    [ObservableProperty] private ExpenseCategory? _category;
    [ObservableProperty] private AccountVM _source = new();
    [ObservableProperty] private TagVM? _tag;
    [ObservableProperty] private SavingGoalVM? _goal;
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private DateTime? _endDate;
    [ObservableProperty] private bool _isOverdue;
}
