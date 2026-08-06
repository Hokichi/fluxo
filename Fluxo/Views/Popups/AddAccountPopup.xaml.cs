using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Fluxo.Services.Notifications;
using Fluxo.ViewModels.Popups;

namespace Fluxo.Views.Popups;

public partial class AddAccountPopup : BasePopup
{
    private readonly AddAccountVM _viewModel;

    public AddAccountPopup(AddAccountVM viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        Loaded += async (_, _) =>
        {
            await _viewModel.LoadDeductSourcesAsync();
            _viewModel.BeginChangeTracking();
            NameTextBox.Focus();
        };
    }

    protected override async void OnSaveButtonClick()
    {
        var result = await _viewModel.SaveAsync();
        if (HandleSaveFailure(result))
            return;

        if (result.ShouldClose)
            Close();
    }

    protected override void OnCloseButtonClick()
    {
        if (_viewModel.HasChanges)
        {
            var confirmation = FluxoMessageBox.Show(this,
                "Close and discard changes?",
                _viewModel.PopupTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmation != MessageBoxResult.Yes)
                return;
        }

        base.OnCloseButtonClick();
    }

    private bool HandleSaveFailure(AddAccountResult result)
    {
        if (result.IsSuccess)
            return false;

        ShowValidationMessage(result.ErrorMessage);
        return true;
    }

    private void FocusPrimaryInput()
    {
        NameTextBox.Focus();
    }

    private void OnNameTextBoxLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _viewModel.ValidateNameField();
    }

    private void OnMaximumSpendingTextBoxLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _viewModel.ValidateMaximumSpendingField();
    }

    private void OnSpentAmountTextBoxLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _viewModel.ValidateSpentAmountField();
    }

    private void OnMinimumPaymentTextBoxLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _viewModel.ValidateMinimumPaymentField();
    }

    private void OnApyTextBoxLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _viewModel.ValidateApyField();
    }

    private void OnMaximumSpendingTextBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not FrameworkElement { IsKeyboardFocusWithin: true })
            return;

        _viewModel.MarkMaximumSpendingModified();
    }

    private void ShowValidationMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        FloatingNotificationPublisher.SaveFailed("Account not saved", [message]);
    }

}
