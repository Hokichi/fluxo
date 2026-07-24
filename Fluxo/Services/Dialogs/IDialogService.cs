using System;
using System.Threading.Tasks;
using System.Windows;
using Fluxo.ViewModels.Popups;
using Fluxo.ViewModels.Popups.Planning;
using Fluxo.ViewModels.Popups.Settings;
using Fluxo.Services.Updates;
using Fluxo.Views.Popups;

namespace Fluxo.Services.Dialogs;

public interface IDialogService
{
    bool? ShowQuickAdd(Window? owner = null);

    bool? ShowHotkeysOverview(Window? owner = null);

    bool? ShowAccountsList(Window? owner = null);

    bool? ShowSettings(Window? owner = null);
    bool? ShowDataManagement(Window? owner = null);

    bool? ShowQuickSetupWizard(Window? owner = null);

    bool? ShowAppUnlock(Func<string, bool> tryUnlock, Window? owner = null);

    bool? ShowPlanningReport(Window? owner = null);

    bool? ShowBudgetForecast(Window? owner = null);

    bool? ShowAddNewTransaction(TransactionPopupRequest request, Window? owner = null);

    bool? ShowAccountDetail(AccountDetailVM viewModel, Window? owner = null);

    bool? ShowTransferFunds(TransferFundsVM viewModel, Window? owner = null);

    bool? ShowAccountReconciliation(AccountReconciliationVM viewModel, Window? owner = null);

    bool? ShowAddAccount(Window? owner = null);

    bool? ShowAddAccount(AddAccountVM viewModel, Window? owner = null);

    bool? ShowAddSavingGoal(Window? owner = null);

    bool? ShowAddSavingGoal(AddSavingGoalVM viewModel, Window? owner = null);


    bool? ShowAddTag(SettingsTagsTabVM settingsViewModel, Window? owner = null);
    bool? ShowAddTag(AddTagVM viewModel, Func<string, string, string, Task<SettingsOperationResult>> saveTagAsync,
        Window? owner = null);
    bool? ShowAddTag(Func<string, string, string, Task<SettingsOperationResult>> createTagAsync, Window? owner = null);

    (bool? DialogResult, string SelectedHexColor) ShowAddTagColorPicker(string initialHexColor, Window? owner = null);

    bool? ShowFeaturePlaceholder(string title, string message, Window? owner = null);

    (bool? DialogResult, DeleteAllDataChoice Choice) ShowDeleteAllData(Window? owner = null);

    Task ShowToastWhileAsync(string message, Func<Task> work, Window? owner = null);

    Task ShowToastWhileAsync(string message, Action work, Window? owner = null);

    Task<string?> ShowDownloadUpdateAsync(
        AppUpdateCheckResult update,
        Func<IProgress<double>, CancellationToken, Task<string>> downloadInstallerAsync,
        Window? owner = null);

    MessageBoxResult ShowWarning(string message, string title, Window? owner = null,
        MessageBoxButton buttons = MessageBoxButton.OK);

    MessageBoxResult ShowError(string message, string title, Window? owner = null,
        MessageBoxButton buttons = MessageBoxButton.OK);

    MessageBoxResult ShowInformation(string message, string title, Window? owner = null,
        MessageBoxButton buttons = MessageBoxButton.OK);

    MessageBoxResult ShowQuestion(string message, string title, Window? owner = null,
        MessageBoxButton buttons = MessageBoxButton.YesNo);
}
