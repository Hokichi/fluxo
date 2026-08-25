using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Entities;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.Logging;
using Fluxo.Services.Notifications;
using Fluxo.Services.Persistence;

using Fluxo.DataModels.Popups.AddSavingGoal;
namespace Fluxo.ViewModels.Popups;

public partial class AddSavingGoalVM : ObservableObject
{
    private readonly IAppDataService _appData;
    private readonly Func<AddSavingGoalInput, Task<AddSavingGoalResult>>? _saveDraftAsync;
    private FormState _initialState;
    private bool _isChangeTrackingInitialized;

    [ObservableProperty] private decimal _currentAmountText;
    [ObservableProperty] private DateTime? _endDate = DateTime.Today.AddMonths(3);
    [ObservableProperty] private bool _hasDefiniteEndDate;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _nameText = string.Empty;
    [ObservableProperty] private decimal _targetAmountText;

    public int? EditingId { get; init; }

    public AddSavingGoalVM(
        IAppDataService appData,
        Func<AddSavingGoalInput, Task<AddSavingGoalResult>>? saveDraftAsync = null)
    {
        _appData = appData;
        _saveDraftAsync = saveDraftAsync;
        _initialState = CaptureState();
    }

    public bool CanSave => !IsBusy && AreRequiredFieldsFilled();
    public bool HasChanges => _isChangeTrackingInitialized && !CaptureState().Equals(_initialState);
    public bool IsEditMode => EditingId.HasValue;
    public string NotificationAction => IsEditMode ? "Updated" : "Added";
    public string PopupTitle => IsEditMode ? "Edit Goal" : "Add Goal";
    public string HeaderTitle => IsEditMode ? "Edit Goal" : "Add Goal";
    public string HeaderDescription => IsEditMode
        ? "Update this savings goal so fluxo can keep tracking the right target."
        : "Add a savings goal so fluxo can track progress from day one.";
    public string ValidationDialogTitle => PopupTitle;

    public void BeginChangeTracking()
    {
        _initialState = CaptureState();
        _isChangeTrackingInitialized = true;
        NotifyFormStateChanged();
    }

    partial void OnCurrentAmountTextChanged(decimal value) => NotifyFormStateChanged();
    partial void OnEndDateChanged(DateTime? value) => NotifyFormStateChanged();
    partial void OnHasDefiniteEndDateChanged(bool value) => NotifyFormStateChanged();
    partial void OnIsBusyChanged(bool value) => NotifyFormStateChanged();
    partial void OnNameTextChanged(string value) => NotifyFormStateChanged();
    partial void OnTargetAmountTextChanged(decimal value) => NotifyFormStateChanged();

    public async Task<AddSavingGoalResult> SaveAsync()
    {
        if (IsBusy)
            return AddSavingGoalResult.Failure("A saving goal is already being saved.");

        if (!TryBuildInput(out var input, out var validationMessage))
            return AddSavingGoalResult.Failure(validationMessage);

        if (_saveDraftAsync is not null)
            return await _saveDraftAsync(input);

        using var persistenceBatch = AppDataPersistenceBatch.Begin(_appData);
        IsBusy = true;

        try
        {
            SavingGoal? createdGoal = null;
            if (EditingId.HasValue)
            {
                var existing = await _appData.GetSavingGoalByIdAsync(EditingId.Value);
                if (existing is null)
                    return AddSavingGoalResult.Failure("Saving goal not found.");

                existing.Name = input.Name;
                existing.TargetAmount = input.TargetAmount;
                existing.CurrentAmount = input.CurrentAmount;
                existing.SavingEndDate = input.EndDate;
                _appData.UpdateSavingGoal(existing);
            }
            else
            {
                var savingGoal = new SavingGoal
                {
                    Name = input.Name,
                    TargetAmount = input.TargetAmount,
                    CurrentAmount = input.CurrentAmount,
                    SavingEndDate = input.EndDate,
                    CreatedOn = DateTime.UtcNow
                };
                await _appData.AddSavingGoalAsync(savingGoal);
                createdGoal = savingGoal;
            }

            await persistenceBatch.SaveChangesAsync();
            try
            {
                if (createdGoal is not null)
                {
                    WeakReferenceMessenger.Default.Send(
                        new NotificationEntityCreatedMessage(NotificationEntityKind.SavingGoal, createdGoal.Id));
                }

                WeakReferenceMessenger.Default.Send(new DashboardDataInvalidatedMessage(
                    DashboardDataInvalidationScope.SavingGoals));
                FloatingNotificationPublisher.Success(
                    input.Name,
                    IsEditMode ? "Saving goal details were updated." : "Saving goal is ready to track.",
                    true,
                    NotificationAction);
            }
            catch (Exception exception)
            {
                FluxoLogManager.LogWarning(
                    exception,
                    "The saving goal was saved, but the current UI could not be refreshed.");
            }

            return AddSavingGoalResult.Success(true);
        }
        catch (Exception exception)
        {
            FloatingNotificationPublisher.LoggedFailure(WeakReferenceMessenger.Default, exception,
                IsEditMode ? "update saving goal" : "create saving goal");
            return AddSavingGoalResult.Failure(string.Empty);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool TryBuildInput(out AddSavingGoalInput input, out string validationMessage)
    {
        input = default;
        validationMessage = string.Empty;

        var name = (NameText ?? string.Empty).Trim();
        var failures = new List<string>();
        if (name.Length == 0)
            failures.Add("Please enter a goal name.");
        if (TargetAmountText <= 0m)
            failures.Add("Please enter a target amount greater than zero.");
        if (CurrentAmountText < 0m)
            failures.Add("Current amount must be zero or greater.");
        if (failures.Count > 0)
        {
            validationMessage = string.Join(Environment.NewLine, failures);
            return false;
        }

        input = new AddSavingGoalInput(
            name,
            TargetAmountText,
            CurrentAmountText,
            HasDefiniteEndDate ? EndDate?.Date : null);
        return true;
    }

    private bool AreRequiredFieldsFilled()
    {
        return !string.IsNullOrWhiteSpace(NameText) &&
               TargetAmountText > 0m;
    }

    private FormState CaptureState()
    {
        return new FormState(
            NameText ?? string.Empty,
            TargetAmountText,
            CurrentAmountText,
            HasDefiniteEndDate,
            HasDefiniteEndDate ? EndDate?.Date : null);
    }

    private void NotifyFormStateChanged()
    {
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(HasChanges));
    }



}
