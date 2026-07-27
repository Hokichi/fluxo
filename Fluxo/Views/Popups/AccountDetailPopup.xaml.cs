using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Fluxo.Services.Dialogs;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Popups;
using Fluxo.Views.Shell;
using Fluxo.Views.Shell.Main;
using Fluxo.Helpers.Popups;

namespace Fluxo.Views.Popups;

public partial class AccountDetailPopup : BasePopup
{
    private readonly IDialogService _dialogService;
    private readonly AccountDetailVM _viewModel;
    private bool _reopenSourcesOnClose;

    public AccountDetailPopup(AccountDetailVM viewModel, IDialogService dialogService)
    {
        InitializeComponent();
        _dialogService = dialogService;
        _viewModel = viewModel;
        DataContext = viewModel;
        Closed += OnPopupClosed;
        Loaded += OnLoadedAsync;
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        if (!await _viewModel.LoadAsync())
        {
            FluxoMessageBox.Show(this, "This account could not be loaded.", "Income Detail",
                MessageBoxButton.OK, MessageBoxImage.Information);
            _ = Dispatcher.BeginInvoke(new System.Action(Close));
            return;
        }

        NameTextBox.Focus();
    }

    private async void OnEditButtonClick(object sender, RoutedEventArgs e)
    {
        var editViewModel = await _viewModel.CreateEditAccountViewModelAsync();
        if (editViewModel is null)
        {
            ShowValidationMessage("Unable to load this account.");
            return;
        }

        _dialogService.ShowAddAccount(editViewModel, this);
        _ = _viewModel.LoadAsync();
    }



    private async void OnDisableButtonClick(object sender, RoutedEventArgs e)
    {
        if (await _viewModel.ShouldConfirmDisablingOnlyEnabledSourceAsync() &&
            FluxoMessageBox.Show(
                this,
                "This is the only working account. fluxo will lock if this account is disabled. Are you sure you want to disable this account?",
                "Income Detail",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var result = await _viewModel.ToggleEnabledAsync();
        if (!result.IsSuccess)
            ShowValidationMessage(result.ErrorMessage);
    }

    private async void OnPinOrUnpinButtonClick(object sender, RoutedEventArgs e)
    {
        var result = await _viewModel.ToggleVisibilityAsync();
        if (!result.IsSuccess)
            ShowValidationMessage(result.ErrorMessage);
    }

    private void OnTransferButtonClick(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanTransfer)
            return;

        var transferVm = new TransferFundsVM(_viewModel.MainViewModel, new AccountVM
        {
            Id = _viewModel.AccountId,
            Name = _viewModel.NameText,
            AccountType = _viewModel.AccountType,
            IsEnabled = _viewModel.IsEnabled,
            PinnedOnUI = _viewModel.PinnedOnUI
        }, _viewModel.AppData);

        _dialogService.ShowTransferFunds(transferVm, this);
        _ = _viewModel.LoadAsync();
    }

    private void OnBackToSourcesButtonClick(object sender, RoutedEventArgs e)
    {
        if (Owner is not MainWindow)
            return;

        _reopenSourcesOnClose = true;
        CloseForPopupHandoff();
    }

    private async void OnDeleteButtonClick(object sender, RoutedEventArgs e)
    {
        var confirmationMessage = await _viewModel.BuildDeleteConfirmationMessageAsync();
        if (FluxoMessageBox.Show(this, confirmationMessage, "Income Detail", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var result = await _viewModel.DeleteAsync();
        if (!result.IsSuccess)
        {
            ShowValidationMessage(result.ErrorMessage);
            return;
        }

        if (result.ShouldClose)
        {
            _ = Dispatcher.BeginInvoke(new System.Action(Close));
        }
    }



    private void ShowValidationMessage(string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            FluxoMessageBox.Show(this, message, "Income Detail", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        if (!_reopenSourcesOnClose || Owner is not MainWindow ownerWindow)
            return;

        _reopenSourcesOnClose = false;
        ownerWindow.Dispatcher.BeginInvoke(new Action(ownerWindow.OpenAccountsListPopup));
    }



    private void OnMonthlyDueDatePreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
            e.Handled = true;
    }

    private void OnMonthlyDueDatePreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is TextBox textBox)
            e.Handled = !WouldResultInValidMonthlyDueDate(textBox, e.Text);
    }

    private void OnMonthlyDueDatePasting(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        if (!e.SourceDataObject.GetDataPresent(DataFormats.Text))
        {
            e.CancelCommand();
            return;
        }

        var pastedText = e.SourceDataObject.GetData(DataFormats.Text) as string ?? string.Empty;
        if (!WouldResultInValidMonthlyDueDate(textBox, pastedText))
            e.CancelCommand();
    }

    private static bool WouldResultInValidMonthlyDueDate(TextBox textBox, string incomingText)
    {
        if (string.IsNullOrEmpty(incomingText))
            return true;

        foreach (var character in incomingText)
            if (!char.IsDigit(character))
                return false;

        var currentText = textBox.Text ?? string.Empty;
        var nextText = currentText
            .Remove(textBox.SelectionStart, textBox.SelectionLength)
            .Insert(textBox.SelectionStart, incomingText);

        if (nextText.Length == 0)
            return true;

        if (!int.TryParse(nextText, out var monthlyDueDate))
            return false;

        return monthlyDueDate is >= MonthlyDueDateHelper.MinMonthlyDay and <= MonthlyDueDateHelper.MaxMonthlyDay;
    }
}
