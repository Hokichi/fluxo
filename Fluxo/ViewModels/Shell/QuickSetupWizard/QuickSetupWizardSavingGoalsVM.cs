using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Entities;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.ViewModels.Popups;
using MainVM = Fluxo.ViewModels.Shell.Main.MainVM;

namespace Fluxo.ViewModels.Shell.QuickSetupWizard;

public partial class QuickSetupWizardSavingGoalsVM : ObservableObject
{
    private readonly MainVM _mainViewModel;
    private readonly IAppDataService _appData;
    private readonly IMessenger _messenger;
    private readonly Dictionary<int, QuickSetupWizardDraftSavingGoal> _draftGoals = [];
    private readonly HashSet<int> _removedPersistedIds = [];
    private int _nextTemporaryId = -1;
    private bool _isLoaded;

    [ObservableProperty] private bool _isStep4Active;

    public QuickSetupWizardSavingGoalsVM(
        MainVM mainViewModel,
        IAppDataService appData,
        IMessenger? messenger = null)
    {
        _mainViewModel = mainViewModel;
        _appData = appData;
        _messenger = messenger ?? WeakReferenceMessenger.Default;
    }

    public ObservableCollection<QuickSetupWizardSavingGoalItemVM> SavingGoals { get; } = [];

    public AddSavingGoalVM CreateAddViewModel()
    {
        return new AddSavingGoalVM(
            _mainViewModel,
            _appData,
            saveDraftAsync: input => SaveDraftGoalAsync(input, null));
    }

    public async Task<AddSavingGoalVM> CreateEditViewModelAsync(int id)
    {
        if (!_isLoaded)
            await LoadDraftGoalsAsync();

        if (!_draftGoals.TryGetValue(id, out var goal))
            return CreateAddViewModel();

        return new AddSavingGoalVM(
            _mainViewModel,
            _appData,
            saveDraftAsync: input => SaveDraftGoalAsync(input, goal.Id))
        {
            EditingId = goal.Id,
            NameText = goal.Name,
            TargetAmountText = goal.TargetAmount,
            CurrentAmountText = goal.CurrentAmount,
            EndDate = goal.SavingEndDate,
            HasDefiniteEndDate = goal.SavingEndDate.HasValue
        };
    }

    public Task DeleteAsync(int id)
    {
        if (id > 0)
            _removedPersistedIds.Add(id);

        _draftGoals.Remove(id);
        RefreshProjectionAndPublish();
        return Task.CompletedTask;
    }

    public async Task RefreshAsync()
    {
        if (!_isLoaded)
            await LoadDraftGoalsAsync();

        RefreshProjectionAndPublish();
    }

    public async Task ApplyAsync(IAppDataService appData)
    {
        var existingGoals = (await appData.GetSavingGoalsAsync())
            .ToDictionary(goal => goal.Id);

        foreach (var draft in _draftGoals.Values.OrderBy(goal => goal.Id))
        {
            if (draft.Id > 0 && existingGoals.TryGetValue(draft.Id, out var persisted))
            {
                persisted.Name = draft.Name;
                persisted.TargetAmount = draft.TargetAmount;
                persisted.CurrentAmount = draft.CurrentAmount;
                persisted.SavingEndDate = draft.SavingEndDate;
                appData.UpdateSavingGoal(persisted);
            }
            else
            {
                await appData.AddSavingGoalAsync(new SavingGoal
                {
                    Name = draft.Name,
                    TargetAmount = draft.TargetAmount,
                    CurrentAmount = draft.CurrentAmount,
                    SavingEndDate = draft.SavingEndDate,
                    CreatedOn = DateTime.UtcNow
                });
            }
        }

        foreach (var removedId in _removedPersistedIds)
        {
            if (existingGoals.TryGetValue(removedId, out var existing))
                appData.RemoveSavingGoal(existing);
        }
    }

    private void PublishSnapshot()
    {
        _messenger.Send(new QuickSetupWizardSavingGoalsChangedMessage(
            new QuickSetupWizardSavingGoalsChanged(SavingGoals.Count)));
    }

    private async Task LoadDraftGoalsAsync()
    {
        var persistedGoals = await _appData.GetSavingGoalsAsync();

        _draftGoals.Clear();
        foreach (var goal in persistedGoals)
        {
            _draftGoals[goal.Id] = new QuickSetupWizardDraftSavingGoal(
                goal.Id,
                goal.Name,
                goal.TargetAmount,
                goal.CurrentAmount,
                goal.SavingEndDate);
        }

        _removedPersistedIds.Clear();
        _nextTemporaryId = -1;
        _isLoaded = true;
    }

    private Task<AddSavingGoalResult> SaveDraftGoalAsync(
        AddSavingGoalInput input,
        int? editingId)
    {
        var id = editingId ?? _nextTemporaryId--;
        _draftGoals[id] = new QuickSetupWizardDraftSavingGoal(
            id,
            input.Name,
            input.TargetAmount,
            input.CurrentAmount,
            input.EndDate);

        if (id > 0)
            _removedPersistedIds.Remove(id);

        RefreshProjectionAndPublish();
        return Task.FromResult(AddSavingGoalResult.Success(true));
    }

    private void RefreshProjectionAndPublish()
    {
        QuickSetupWizardShared.ReplaceCollection(
            SavingGoals,
            _draftGoals.Values
                .OrderBy(goal => goal.SavingEndDate ?? DateTime.MaxValue)
                .ThenBy(goal => goal.Name)
                .Select(goal => new QuickSetupWizardSavingGoalItemVM(goal)));

        PublishSnapshot();
    }
}
