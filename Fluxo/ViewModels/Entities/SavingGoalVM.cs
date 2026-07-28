using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using System.Globalization;
using Fluxo.Resources.Resources.Messages;

namespace Fluxo.ViewModels.Entities;

public partial class SavingGoalVM : ObservableRecipient, IRecipient<StartupNotificationStateChangedMessage>
{
    public SavingGoalVM() : base(WeakReferenceMessenger.Default)
    {
        base.IsActive = true;
    }

    public void Receive(StartupNotificationStateChangedMessage message) =>
        IsOverdue = message.Value.OverdueSavingGoalIds.Contains(Id);

    [ObservableProperty] private DateTime _createdOn;
    [ObservableProperty] private decimal _currentAmount;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private decimal _remainingAmount;
    [ObservableProperty] private int _id;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private DateTime? _savingEndDate;
    [ObservableProperty] private decimal _targetAmount;
    [ObservableProperty] private bool _isOverdue;

    public string AmountLeftText => FormatMoneyAmount(RemainingAmount);
    public string WeeklyAverageText => FormatMoneyAmount(Math.Ceiling(CurrentAmount / GetElapsedCompletedWeeks()));
    public string EstimatedDeadlineText => SavingEndDate?.ToString("MMM d, yyyy", CultureInfo.CurrentCulture) ?? "Indefinite";
    public decimal ProgressRatio => TargetAmount <= 0 ? 0m : Math.Clamp(CurrentAmount / TargetAmount, 0m, 1m);
    public int ProgressPercentage => (int)Math.Round(ProgressRatio * 100, MidpointRounding.AwayFromZero);

    partial void OnCurrentAmountChanged(decimal oldValue, decimal newValue)
    {
        if (TargetAmount == 0)
            return;

        RemainingAmount = TargetAmount - CurrentAmount;
        OnPropertyChanged(nameof(AmountLeftText));
        OnPropertyChanged(nameof(WeeklyAverageText));
    }

    partial void OnTargetAmountChanged(decimal oldValue, decimal newValue)
    {
        if (TargetAmount == 0)
            return;

        RemainingAmount = TargetAmount - CurrentAmount;
        OnPropertyChanged(nameof(AmountLeftText));
    }

    partial void OnCreatedOnChanged(DateTime value)
    {
        OnPropertyChanged(nameof(WeeklyAverageText));
    }

    partial void OnSavingEndDateChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(EstimatedDeadlineText));
    }

    partial void OnCurrentAmountChanged(decimal value)
    {
        NotifyProgressChanged();
    }

    partial void OnTargetAmountChanged(decimal value)
    {
        NotifyProgressChanged();
    }

    private void NotifyProgressChanged()
    {
        OnPropertyChanged(nameof(ProgressRatio));
        OnPropertyChanged(nameof(ProgressPercentage));
        OnPropertyChanged(nameof(AmountLeftText));
        OnPropertyChanged(nameof(WeeklyAverageText));
    }

    private int GetElapsedCompletedWeeks()
    {
        var elapsedDays = Math.Max(0, (DateTime.Today - CreatedOn.Date).Days);
        return Math.Max(1, elapsedDays / 7);
    }

    private static string FormatMoneyAmount(decimal amount)
    {
        return amount.ToString("N0", CultureInfo.CurrentCulture);
    }
}
