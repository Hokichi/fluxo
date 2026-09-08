using System.Collections.ObjectModel;
using System.Globalization;
using AutoMapper;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Constants;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.ViewModels.Entities;

namespace Fluxo.ViewModels.Shell.Main;

public partial class SavingGoalsPanelVM : ObservableRecipient, IRecipient<DashboardDataInvalidatedMessage>
{
    private readonly IAppDataService _appData;
    private readonly IMapper _mapper;
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private readonly HashSet<int> _disabledSavingGoalIds = [];
    private readonly HashSet<int> _hiddenSavingGoalIds = [];

    public SavingGoalsPanelVM(
        IAppDataService appData,
        IMapper mapper,
        IMessenger? messenger = null)
        : base(messenger ?? WeakReferenceMessenger.Default)
    {
        _appData = appData;
        _mapper = mapper;
        IsActive = true;
    }

    [ObservableProperty]
    private bool _hasSavingGoals;

    [ObservableProperty]
    private bool _hasMultipleSavingGoals;

    [ObservableProperty]
    private int _currentGoalIndex = -1;

    [ObservableProperty]
    private SavingGoalVM? _currentGoal;

    [ObservableProperty]
    private int _navigationDirection;

    public ObservableCollection<SavingGoalVM> SavingGoals { get; } = [];
    public int GoalStepCount => SavingGoals.Count;
    public int CurrentStepNumber => HasSavingGoals && CurrentGoalIndex >= 0 ? CurrentGoalIndex + 1 : 0;

    public void Receive(DashboardDataInvalidatedMessage message)
    {
        if (!message.Value.HasFlag(DashboardDataInvalidationScope.SavingGoals))
            return;

        _ = ReloadFromServicesAsync();
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await LoadSavingGoalSettingsAsync(cancellationToken);

        var savingGoals = _mapper.Map<IReadOnlyList<SavingGoalVM>>(
            await _appData.GetSavingGoalsAsync(cancellationToken));

        var previousGoalId = CurrentGoal?.Id;

        SavingGoals.Clear();

        foreach (var goal in savingGoals.Where(goal =>
                     goal.ProgressRatio < 1m &&
                     !_hiddenSavingGoalIds.Contains(goal.Id) &&
                     !_disabledSavingGoalIds.Contains(goal.Id)))
            SavingGoals.Add(goal);

        OnPropertyChanged(nameof(GoalStepCount));
        HasSavingGoals = SavingGoals.Count > 0;
        HasMultipleSavingGoals = SavingGoals.Count > 1;

        if (!HasSavingGoals)
        {
            CurrentGoalIndex = -1;
            CurrentGoal = null;
            NavigationDirection = 0;
            return;
        }

        var initialIndex = previousGoalId.HasValue
            ? SavingGoals.ToList().FindIndex(goal => goal.Id == previousGoalId.Value)
            : -1;

        SetCurrentGoalByIndex(initialIndex >= 0 ? initialIndex : 0, animateDirection: 0);
    }

    partial void OnHasSavingGoalsChanged(bool value)
    {
        OnPropertyChanged(nameof(CurrentStepNumber));
    }

    partial void OnCurrentGoalIndexChanged(int value)
    {
        OnPropertyChanged(nameof(CurrentStepNumber));
    }

    [RelayCommand]
    public void NavigatePrevious()
    {
        NavigateByOffset(-1, slideDirection: 1);
    }

    [RelayCommand]
    public void NavigateNext()
    {
        NavigateByOffset(1, slideDirection: -1);
    }

    private async Task ReloadFromServicesAsync()
    {
        await _reloadGate.WaitAsync();

        try
        {
            await LoadAsync();
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    private async Task LoadSavingGoalSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await _appData.GetUserSettingsAsync(cancellationToken);
        var settingsByName = settings.ToDictionary(
            setting => setting.Name,
            setting => setting.Value,
            StringComparer.Ordinal);

        _hiddenSavingGoalIds.Clear();
        _hiddenSavingGoalIds.UnionWith(ParseIdSet(settingsByName, UserSettingNames.HiddenSavingGoalIds));

        _disabledSavingGoalIds.Clear();
        _disabledSavingGoalIds.UnionWith(ParseIdSet(settingsByName, UserSettingNames.DisabledSavingGoalIds));
    }

    private static IReadOnlyCollection<int> ParseIdSet(IReadOnlyDictionary<string, string> settings, string name)
    {
        if (!settings.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
            return [];

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                ? id
                : -1)
            .Where(id => id > 0)
            .Distinct()
            .ToArray();
    }

    private void NavigateByOffset(int offset, int slideDirection)
    {
        if (SavingGoals.Count == 0)
            return;

        if (SavingGoals.Count == 1)
        {
            SetCurrentGoalByIndex(0, animateDirection: 0);
            return;
        }

        var normalizedCurrentIndex = CurrentGoalIndex >= 0 ? CurrentGoalIndex : 0;
        var targetIndex = (normalizedCurrentIndex + offset + SavingGoals.Count) % SavingGoals.Count;
        SetCurrentGoalByIndex(targetIndex, slideDirection);
    }

    private void SetCurrentGoalByIndex(int index, int animateDirection)
    {
        if (SavingGoals.Count == 0)
        {
            CurrentGoalIndex = -1;
            CurrentGoal = null;
            NavigationDirection = 0;
            UpdateActiveGoal();
            return;
        }

        if (index < 0 || index >= SavingGoals.Count)
            index = 0;

        CurrentGoalIndex = index;
        NavigationDirection = animateDirection;
        CurrentGoal = SavingGoals[index];
        UpdateActiveGoal();
    }

    private void UpdateActiveGoal()
    {
        for (var index = 0; index < SavingGoals.Count; index++)
            SavingGoals[index].IsActive = index == CurrentGoalIndex;
    }
}
