using CommunityToolkit.Mvvm.ComponentModel;
using Fluxo.Helpers.MainWindow;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Enums;
using Fluxo.Services.Dialogs;
using Fluxo.Services.Ui;
using System.Windows;
using Fluxo.Resources.Resources.Messages;

namespace Fluxo.ViewModels.Shell.Main;

public partial class MainViewModeToggleVM : ObservableRecipient,
    IRecipient<SpinnerPeriodStateChangedMessage>,
    IRecipient<ViewModeChangeMessage>
{
    private readonly IDialogService? _dialogService;
    private readonly IUiSettleAwaiter? _uiSettleAwaiter;
    private readonly SemaphoreSlim _viewModeTransitionGate = new(1, 1);

    [ObservableProperty]
    private MainContentViewMode _selectedMainContentViewMode = MainContentViewMode.Daily;

    [ObservableProperty]
    private string _moveToCurrentLabel = "Move to today";

    [ObservableProperty]
    private bool _isAtCurrentPeriod = true;

    [ObservableProperty]
    private bool _isSpinnerVisible = true;

    public MainViewModeToggleVM(
        IMessenger? messenger = null,
        IDialogService? dialogService = null,
        IUiSettleAwaiter? uiSettleAwaiter = null)
        : base(messenger ?? WeakReferenceMessenger.Default)
    {
        _dialogService = dialogService;
        _uiSettleAwaiter = uiSettleAwaiter;
        IsActive = true;
    }

    public bool IsDailyViewSelected => SelectedMainContentViewMode == MainContentViewMode.Daily;

    public bool IsWeeklyViewSelected => SelectedMainContentViewMode == MainContentViewMode.Weekly;

    public bool IsMonthlyViewSelected => SelectedMainContentViewMode == MainContentViewMode.Monthly;

    public bool IsAllocationPeriodViewSelected => SelectedMainContentViewMode == MainContentViewMode.AllocationPeriod;

    public bool IsAllTimeViewSelected => SelectedMainContentViewMode == MainContentViewMode.AllTime;

    [RelayCommand]
    private void SetSelectedMainContentView(MainContentViewMode viewMode)
    {
        SelectedMainContentViewMode = viewMode;
        Messenger.Send(new ViewModeChangeMessage(viewMode));
    }

    [RelayCommand]
    private void MoveToCurrentPeriod()
    {
        Messenger.Send(new MoveToCurrentPeriodRequestedMessage());
    }

    public async Task MoveToCurrentPeriodFromUserAsync(Window? owner = null)
    {
        if (IsAtCurrentPeriod)
            return;

        if (_dialogService is null || _uiSettleAwaiter is null)
        {
            MoveToCurrentPeriod();
            return;
        }

        await _viewModeTransitionGate.WaitAsync();
        try
        {
            await _dialogService.ShowToastWhileAsync(
                MainDataLoadingMessageFormatter.Build(SelectedMainContentViewMode, DateTime.Today),
                async () =>
                {
                    MoveToCurrentPeriod();
                    await _uiSettleAwaiter.WaitForUiReadyAsync(owner);
                },
                owner);
        }
        finally
        {
            _viewModeTransitionGate.Release();
        }
    }

    public void Receive(SpinnerPeriodStateChangedMessage message)
    {
        IsAtCurrentPeriod = message.Value.IsAtCurrentPeriod;
        IsSpinnerVisible = message.Value.IsSpinnerVisible;
        MoveToCurrentLabel = message.Value.MoveToCurrentLabel;
    }

    public void Receive(ViewModeChangeMessage message)
    {
        SelectedMainContentViewMode = message.Value;
    }

    public async Task SetSelectedMainContentViewFromUserAsync(MainContentViewMode viewMode, Window? owner = null)
    {
        if (viewMode == SelectedMainContentViewMode)
            return;

        if (_dialogService is null || _uiSettleAwaiter is null)
        {
            SetSelectedMainContentView(viewMode);
            return;
        }

        await _viewModeTransitionGate.WaitAsync();
        try
        {
            await _dialogService.ShowToastWhileAsync(
                MainDataLoadingMessageFormatter.Build(viewMode, DateTime.Today),
                async () =>
                {
                    SetSelectedMainContentView(viewMode);
                    await _uiSettleAwaiter.WaitForUiReadyAsync(owner);
                },
                owner);
        }
        finally
        {
            _viewModeTransitionGate.Release();
        }
    }

    partial void OnSelectedMainContentViewModeChanged(MainContentViewMode oldValue, MainContentViewMode newValue)
    {
        OnPropertyChanged(nameof(IsDailyViewSelected));
        OnPropertyChanged(nameof(IsWeeklyViewSelected));
        OnPropertyChanged(nameof(IsMonthlyViewSelected));
        OnPropertyChanged(nameof(IsAllocationPeriodViewSelected));
        OnPropertyChanged(nameof(IsAllTimeViewSelected));
    }
}
