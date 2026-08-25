using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Budgeting;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.History;
using Fluxo.ViewModels.Shell;
using MainVM = Fluxo.ViewModels.Shell.Main.MainVM;

using BudgetAllocationSnapshot = Fluxo.DataModels.Popups.Settings.BudgetAllocationSnapshot.BudgetAllocationSnapshot;
using Fluxo.DataModels.Popups.Settings.SettingsBudgetTab;
namespace Fluxo.ViewModels.Popups.Settings;

public partial class SettingsBudgetTabVM : ObservableObject
{
    private readonly BudgetAllocationBalancer _allocationBalancer = new();
    private readonly Func<decimal> _totalBudgetAmountProvider;
    private readonly Func<DateTime> _todayProvider;
    private readonly IMessenger _messenger;
    private readonly IAppDataService _appData;
    private bool _isApplyingAllocation;
    private bool _suppressPendingStatePublish;
    private BudgetAllocationSnapshot _savedBudgetAllocation = new(
        50,
        30,
        20,
        0m,
        AllocationPeriod.Monthly,
        1,
        RolloverPolicy.None,
        OverspendPolicy.Ignore);

    [ObservableProperty] private decimal _allocationLimit;
    [ObservableProperty] private AllocationPeriod _allocationPeriod = AllocationPeriod.Monthly;
    [ObservableProperty] private string _budgetAllocationErrorMessage = string.Empty;
    [ObservableProperty] private int _investAllocationPercentage;
    [ObservableProperty] private int _needsAllocationPercentage;
    [ObservableProperty] private OverspendPolicy _overspendPolicy = OverspendPolicy.Ignore;
    [ObservableProperty] private int _periodStart = 1;
    [ObservableProperty] private RolloverPolicy _rolloverPolicy = RolloverPolicy.None;
    [ObservableProperty] private SettingsBudgetManagementPage _selectedBudgetManagementPage =
        SettingsBudgetManagementPage.Allocation;
    [ObservableProperty] private int _wantsAllocationPercentage;

    public SettingsBudgetTabVM(
        MainVM mainViewModel,
        IAppDataService appData,
        IMessenger? messenger = null,
        Func<DateTime>? todayProvider = null)
        : this(() => mainViewModel.BudgetPanel.TotalIncomeAmount, appData, messenger, todayProvider)
    {
    }

    public SettingsBudgetTabVM(
        Func<decimal> totalBudgetAmountProvider,
        IAppDataService appData,
        IMessenger? messenger = null,
        Func<DateTime>? todayProvider = null)
    {
        _totalBudgetAmountProvider = totalBudgetAmountProvider;
        _appData = appData;
        _messenger = messenger ?? WeakReferenceMessenger.Default;
        _todayProvider = todayProvider ?? (() => DateTime.Today);
    }

    public decimal TotalBudgetAmount => _totalBudgetAmountProvider();
    public IReadOnlyList<PeriodStartOption> WeekdayPeriodStartOptions { get; } =
    [
        new(1, "Monday"),
        new(2, "Tuesday"),
        new(3, "Wednesday"),
        new(4, "Thursday"),
        new(5, "Friday"),
        new(6, "Saturday"),
        new(7, "Sunday")
    ];

    public IReadOnlyList<PeriodStartOption> QuarterlyPeriodStartOptions { get; } =
    [
        new(1, "Month 1"),
        new(2, "Month 2"),
        new(3, "Month 3")
    ];

    public IReadOnlyList<PeriodStartOption> YearlyPeriodStartOptions { get; } =
    [
        new(1, "January"),
        new(2, "February"),
        new(3, "March"),
        new(4, "April"),
        new(5, "May"),
        new(6, "June"),
        new(7, "July"),
        new(8, "August"),
        new(9, "September"),
        new(10, "October"),
        new(11, "November"),
        new(12, "December")
    ];

    public bool HasBudgetAllocationError => !string.IsNullOrWhiteSpace(BudgetAllocationErrorMessage);
    public bool HasAccounts { get; private set; }
    public bool IsAllocationPageSelected =>
        SelectedBudgetManagementPage == SettingsBudgetManagementPage.Allocation;

    public bool IsConfigurationPageSelected =>
        SelectedBudgetManagementPage == SettingsBudgetManagementPage.Configuration;

    public bool IsWeekdayPeriodStartVisible =>
        AllocationPeriod is AllocationPeriod.Weekly or AllocationPeriod.Biweekly;

    public bool IsMonthlyPeriodStartVisible => AllocationPeriod == AllocationPeriod.Monthly;
    public bool IsQuarterlyPeriodStartVisible => AllocationPeriod == AllocationPeriod.Quarterly;
    public bool IsYearlyPeriodStartVisible => AllocationPeriod == AllocationPeriod.Yearly;

    public bool HasPendingChanges =>
        NeedsAllocationPercentage != _savedBudgetAllocation.Needs ||
        WantsAllocationPercentage != _savedBudgetAllocation.Wants ||
        InvestAllocationPercentage != _savedBudgetAllocation.Invest ||
        AllocationLimit != _savedBudgetAllocation.AllocationLimit ||
        AllocationPeriod != _savedBudgetAllocation.AllocationPeriod ||
        PeriodStart != _savedBudgetAllocation.PeriodStart ||
        RolloverPolicy != _savedBudgetAllocation.RolloverPolicy ||
        OverspendPolicy != _savedBudgetAllocation.OverspendPolicy;

    public bool HasPendingAllocationChanges =>
        NeedsAllocationPercentage != _savedBudgetAllocation.Needs ||
        WantsAllocationPercentage != _savedBudgetAllocation.Wants ||
        InvestAllocationPercentage != _savedBudgetAllocation.Invest;

    public bool HasPendingConfigurationChanges =>
        AllocationLimit != _savedBudgetAllocation.AllocationLimit ||
        AllocationPeriod != _savedBudgetAllocation.AllocationPeriod ||
        PeriodStart != _savedBudgetAllocation.PeriodStart ||
        RolloverPolicy != _savedBudgetAllocation.RolloverPolicy ||
        OverspendPolicy != _savedBudgetAllocation.OverspendPolicy;

    public bool CanSaveConfiguration => !HasBudgetAllocationError;

    public string ConfigurationErrorMessage => BudgetAllocationErrorMessage;

    public string NeedsAllocationAmountText => BuildAllocationAmountText(NeedsAllocationPercentage);
    public string WantsAllocationAmountText => BuildAllocationAmountText(WantsAllocationPercentage);
    public string InvestAllocationAmountText => BuildAllocationAmountText(InvestAllocationPercentage);

    public async Task LoadAsync()
    {
        var allocation = await _appData.GetBudgetAllocationAsync();
        _suppressPendingStatePublish = true;
        try
        {
            ApplyAllocationPercentages(
                allocation.NeedsThreshold,
                allocation.WantsThreshold,
                allocation.InvestThreshold);
            AllocationLimit = allocation.AllocationLimit;
            AllocationPeriod = allocation.AllocationPeriod;
            PeriodStart = BudgetAllocationPeriodRules.ClampPeriodStart(AllocationPeriod, allocation.PeriodStart);
            RolloverPolicy = allocation.RolloverPolicy;
            OverspendPolicy = allocation.OverspendPolicy;

            _savedBudgetAllocation = new BudgetAllocationSnapshot(
                NeedsAllocationPercentage,
                WantsAllocationPercentage,
                InvestAllocationPercentage,
                AllocationLimit,
                AllocationPeriod,
                PeriodStart,
                RolloverPolicy,
                OverspendPolicy);

            HasAccounts = (await _appData.GetAccountsAsync()).Count > 0;
            ValidateBudgetAllocation();
            OnPropertyChanged(nameof(HasAccounts));
            OnPropertyChanged(nameof(TotalBudgetAmount));
            OnPropertyChanged(nameof(NeedsAllocationAmountText));
            OnPropertyChanged(nameof(WantsAllocationAmountText));
            OnPropertyChanged(nameof(InvestAllocationAmountText));
        }
        finally
        {
            _suppressPendingStatePublish = false;
        }

        PublishPendingState();
        RaisePendingCategoryProperties();
    }

    public async Task<(
        SettingsOperationResult Result,
        List<ILogMemoryAction> Actions,
        BudgetAllocationSnapshot Snapshot)> BuildApplyChangesAsync()
    {
        ValidateBudgetAllocation();
        if (HasBudgetAllocationError)
            return (SettingsOperationResult.Failure(BudgetAllocationErrorMessage), [], _savedBudgetAllocation);

        var snapshot = CaptureCurrentSnapshot();
        var allocation = await _appData.GetBudgetAllocationAsync();
        allocation.NeedsThreshold = snapshot.Needs;
        allocation.WantsThreshold = snapshot.Wants;
        allocation.InvestThreshold = snapshot.Invest;
        allocation.AllocationLimit = snapshot.AllocationLimit;
        allocation.AllocationPeriod = snapshot.AllocationPeriod;
        allocation.PeriodStart = BudgetAllocationPeriodRules.ClampPeriodStart(
            snapshot.AllocationPeriod,
            snapshot.PeriodStart);
        allocation.RolloverPolicy = snapshot.RolloverPolicy;
        allocation.OverspendPolicy = snapshot.OverspendPolicy;
        MarkCurrentPeriodWhenRolloverPolicyChangesToEnabled(allocation, snapshot.RolloverPolicy);
        _appData.UpdateBudgetAllocation(allocation);

        return (SettingsOperationResult.Success(), [], snapshot);
    }

    public void CommitSavedState(BudgetAllocationSnapshot snapshot)
    {
        _savedBudgetAllocation = snapshot;
        PublishPendingState();
        RaisePendingCategoryProperties();
    }

    public void OpenAddAccount()
    {
        _messenger.Send(new SettingsDialogRequestedMessage(
            new SettingsDialogRequest(SettingsDialogRequestType.AddAccount)));
    }

    public void RevertChanges()
    {
        ApplyAllocationPercentages(
            _savedBudgetAllocation.Needs,
            _savedBudgetAllocation.Wants,
            _savedBudgetAllocation.Invest);
        AllocationLimit = _savedBudgetAllocation.AllocationLimit;
        AllocationPeriod = _savedBudgetAllocation.AllocationPeriod;
        PeriodStart = _savedBudgetAllocation.PeriodStart;
        RolloverPolicy = _savedBudgetAllocation.RolloverPolicy;
        OverspendPolicy = _savedBudgetAllocation.OverspendPolicy;
        ValidateBudgetAllocation();
        RaisePendingCategoryProperties();
        PublishPendingState();
    }

    public void IncrementAllocation(BudgetAllocationSegment segment, int delta)
    {
        SetAllocation(segment, GetAllocation(segment) + delta);
    }

    public void SetAllocation(BudgetAllocationSegment segment, double value)
    {
        ApplyBalancedAllocation(segment, GetAllocation(segment), (int)Math.Round(value, MidpointRounding.AwayFromZero));
    }

    partial void OnNeedsAllocationPercentageChanged(int oldValue, int newValue)
    {
        ApplyBalancedAllocation(BudgetAllocationSegment.Needs, oldValue, newValue);
    }

    partial void OnWantsAllocationPercentageChanged(int oldValue, int newValue)
    {
        ApplyBalancedAllocation(BudgetAllocationSegment.Wants, oldValue, newValue);
    }

    partial void OnInvestAllocationPercentageChanged(int oldValue, int newValue)
    {
        ApplyBalancedAllocation(BudgetAllocationSegment.Invest, oldValue, newValue);
    }

    partial void OnAllocationLimitChanged(decimal value)
    {
        RaiseAmountProperties();
        OnPropertyChanged(nameof(HasPendingConfigurationChanges));
        PublishPendingState();
    }

    partial void OnAllocationPeriodChanged(AllocationPeriod value)
    {
        PeriodStart = BudgetAllocationPeriodRules.ClampPeriodStart(value, PeriodStart);
        OnPropertyChanged(nameof(IsWeekdayPeriodStartVisible));
        OnPropertyChanged(nameof(IsMonthlyPeriodStartVisible));
        OnPropertyChanged(nameof(IsQuarterlyPeriodStartVisible));
        OnPropertyChanged(nameof(IsYearlyPeriodStartVisible));
        OnPropertyChanged(nameof(HasPendingConfigurationChanges));
        PublishPendingState();
    }

    partial void OnPeriodStartChanged(int value)
    {
        var clamped = BudgetAllocationPeriodRules.ClampPeriodStart(AllocationPeriod, value);
        if (clamped != value)
        {
            PeriodStart = clamped;
            return;
        }

        OnPropertyChanged(nameof(HasPendingConfigurationChanges));
        PublishPendingState();
    }

    partial void OnRolloverPolicyChanged(RolloverPolicy value)
    {
        OnPropertyChanged(nameof(HasPendingConfigurationChanges));
        PublishPendingState();
    }

    partial void OnOverspendPolicyChanged(OverspendPolicy value)
    {
        OnPropertyChanged(nameof(HasPendingConfigurationChanges));
        PublishPendingState();
    }

    partial void OnSelectedBudgetManagementPageChanged(SettingsBudgetManagementPage value)
    {
        OnPropertyChanged(nameof(IsAllocationPageSelected));
        OnPropertyChanged(nameof(IsConfigurationPageSelected));
        PublishPendingState();
    }

    private string BuildAllocationAmountText(int percentage)
    {
        var allocatedAmount = decimal.Round(ResolveAllocationBaseAmount() * percentage / 100m, 0);
        return allocatedAmount.ToString("N0", CultureInfo.CurrentCulture);
    }

    private void OnAllocationChanged()
    {
        ValidateBudgetAllocation();
        RaiseAmountProperties();
        OnPropertyChanged(nameof(HasPendingAllocationChanges));
        PublishPendingState();
    }

    private int GetAllocation(BudgetAllocationSegment segment) => segment switch
    {
        BudgetAllocationSegment.Needs => NeedsAllocationPercentage,
        BudgetAllocationSegment.Wants => WantsAllocationPercentage,
        BudgetAllocationSegment.Invest => InvestAllocationPercentage,
        _ => 0
    };

    private void ApplyBalancedAllocation(BudgetAllocationSegment segment, int oldValue, int newValue)
    {
        if (_isApplyingAllocation)
            return;

        var balanced = _allocationBalancer.Balance(
            segment == BudgetAllocationSegment.Needs ? oldValue : NeedsAllocationPercentage,
            segment == BudgetAllocationSegment.Wants ? oldValue : WantsAllocationPercentage,
            segment == BudgetAllocationSegment.Invest ? oldValue : InvestAllocationPercentage,
            segment,
            newValue);

        ApplyAllocationPercentages(balanced.Needs, balanced.Wants, balanced.Invest);
    }

    private void ApplyAllocationPercentages(int needs, int wants, int invest)
    {
        _isApplyingAllocation = true;
        try
        {
            NeedsAllocationPercentage = needs;
            WantsAllocationPercentage = wants;
            InvestAllocationPercentage = invest;
        }
        finally
        {
            _isApplyingAllocation = false;
        }

        OnAllocationChanged();
    }

    private decimal ResolveAllocationBaseAmount()
    {
        return AllocationLimit > 0m ? AllocationLimit : TotalBudgetAmount;
    }

    private void RaiseAmountProperties()
    {
        OnPropertyChanged(nameof(NeedsAllocationAmountText));
        OnPropertyChanged(nameof(WantsAllocationAmountText));
        OnPropertyChanged(nameof(InvestAllocationAmountText));
    }

    private void RaisePendingCategoryProperties()
    {
        OnPropertyChanged(nameof(HasPendingAllocationChanges));
        OnPropertyChanged(nameof(HasPendingConfigurationChanges));
    }

    private void ValidateBudgetAllocation()
    {
        var total = NeedsAllocationPercentage + WantsAllocationPercentage + InvestAllocationPercentage;
        BudgetAllocationErrorMessage = total == 100
            ? string.Empty
            : $"Needs, Wants, and Invest must add up to 100%. Current total: {total}%";
        OnPropertyChanged(nameof(HasBudgetAllocationError));
    }

    private void PublishPendingState()
    {
        if (_suppressPendingStatePublish)
            return;

        _messenger.Send(new SettingsPendingChangesChangedMessage(
            new SettingsPendingChangesChanged(SettingsTabKey.Budget, HasPendingChanges)));
    }

    private BudgetAllocationSnapshot CaptureCurrentSnapshot() => new(
        NeedsAllocationPercentage,
        WantsAllocationPercentage,
        InvestAllocationPercentage,
        AllocationLimit,
        AllocationPeriod,
        PeriodStart,
        RolloverPolicy,
        OverspendPolicy);

    private void MarkCurrentPeriodWhenRolloverPolicyChangesToEnabled(
        BudgetAllocation allocation,
        RolloverPolicy rolloverPolicy)
    {
        if (rolloverPolicy == RolloverPolicy.None ||
            rolloverPolicy == _savedBudgetAllocation.RolloverPolicy)
            return;

        var currentPeriod = BudgetAllocationPeriodRules.ResolveCurrentPeriod(
            allocation.AllocationPeriod,
            _todayProvider().Date,
            allocation.PeriodStart);
        allocation.LastRolloverPeriodStart = currentPeriod.Start;
    }

}
