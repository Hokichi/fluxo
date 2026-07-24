using System;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Fluxo.Core.Interfaces.Services;
using Fluxo.ViewModels.Popups;
using Fluxo.ViewModels.Popups.Planning;
using Fluxo.ViewModels.Popups.Settings;
using Fluxo.Services.Updates;
using Fluxo.Views.Popups;
using Fluxo.Views.Popups.Planning;
using Fluxo.Views.Popups.Settings;
using Fluxo.Views.Shell.Wizard;
using Microsoft.Extensions.DependencyInjection;

namespace Fluxo.Services.Dialogs;

public sealed class DialogService : IDialogService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Func<Window?, string, string, MessageBoxButton, MessageBoxImage, MessageBoxResult> _showMessageBox;

    public DialogService(IServiceProvider serviceProvider)
        : this(serviceProvider, FluxoMessageBox.Show)
    {
    }

    public DialogService(IServiceProvider serviceProvider,
        Func<Window?, string, string, MessageBoxButton, MessageBoxImage, MessageBoxResult> showMessageBox)
    {
        _serviceProvider = serviceProvider;
        _showMessageBox = showMessageBox;
    }

    public bool? ShowQuickAdd(Window? owner = null)
    {
        return ShowScopedDialog<QuickAddPopup>(owner);
    }

    public bool? ShowHotkeysOverview(Window? owner = null)
    {
        return ShowScopedDialog<HotkeysOverviewPopup>(owner);
    }

    public bool? ShowAccountsList(Window? owner = null)
    {
        return ShowScopedDialog<AccountsListPopup>(owner);
    }

    public bool? ShowSettings(Window? owner = null)
    {
        return ShowScopedDialog<SettingsPopup>(owner);
    }

    public bool? ShowDataManagement(Window? owner = null)
    {
        return ShowScopedDialog<DataManagementPopup>(owner);
    }

    public bool? ShowQuickSetupWizard(Window? owner = null)
    {
        return ShowScopedDialog<QuickSetupWizard>(owner);
    }

    public bool? ShowAppUnlock(Func<string, bool> tryUnlock, Window? owner = null)
    {
        return ShowDialog(new AppUnlockPopup(tryUnlock), owner);
    }

    public bool? ShowPlanningReport(Window? owner = null)
    {
        using var scope = _serviceProvider.CreateScope();
        var viewModel = ActivatorUtilities.CreateInstance<PlanningReportVM>(scope.ServiceProvider);
        var popup = ActivatorUtilities.CreateInstance<PlanningReportPopup>(scope.ServiceProvider, viewModel);
        return ShowDialog(popup, owner);
    }

    public bool? ShowBudgetForecast(Window? owner = null)
    {
        using var scope = _serviceProvider.CreateScope();
        var viewModel = ActivatorUtilities.CreateInstance<BudgetForecastVM>(scope.ServiceProvider);
        var popup = ActivatorUtilities.CreateInstance<BudgetForecastPopup>(scope.ServiceProvider, viewModel);
        return ShowDialog(popup, owner);
    }

    public bool? ShowAddNewTransaction(TransactionPopupRequest request, Window? owner = null)
    {
        using var scope = _serviceProvider.CreateScope();
        var popup = scope.ServiceProvider.GetRequiredService<TransactionPopup>();
        popup.Configure(request);
        return ShowDialog(popup, owner);
    }

    public bool? ShowAccountDetail(AccountDetailVM viewModel, Window? owner = null)
    {
        return ShowDialog(new AccountDetailPopup(viewModel, this), owner);
    }

    public bool? ShowTransferFunds(TransferFundsVM viewModel, Window? owner = null)
    {
        return ShowDialog(new TransferFundsPopup(viewModel), owner);
    }

    public bool? ShowAccountReconciliation(AccountReconciliationVM viewModel, Window? owner = null)
    {
        return ShowDialog(new AccountReconciliationPopup(viewModel), owner);
    }

    public bool? ShowAddAccount(Window? owner = null)
    {
        return ShowScopedDialog<AddAccountPopup>(owner);
    }

    public bool? ShowAddAccount(AddAccountVM viewModel, Window? owner = null)
    {
        return ShowDialog(new AddAccountPopup(viewModel), owner);
    }

    public bool? ShowAddSavingGoal(Window? owner = null)
    {
        return ShowScopedDialog<AddSavingGoalPopup>(owner);
    }

    public bool? ShowAddSavingGoal(AddSavingGoalVM viewModel, Window? owner = null)
    {
        return ShowDialog(new AddSavingGoalPopup(viewModel), owner);
    }


    public bool? ShowAddTag(SettingsTagsTabVM settingsViewModel, Window? owner = null)
    {
        return ShowDialog(new AddTagPopup(settingsViewModel, this), owner);
    }

    public bool? ShowAddTag(AddTagVM viewModel, Func<string, string, string, Task<SettingsOperationResult>> saveTagAsync,
        Window? owner = null)
    {
        return ShowDialog(new AddTagPopup(this, viewModel, saveTagAsync), owner);
    }

    public bool? ShowAddTag(Func<string, string, string, Task<SettingsOperationResult>> createTagAsync, Window? owner = null)
    {
        return ShowDialog(new AddTagPopup(this, createTagAsync), owner);
    }

    public (bool? DialogResult, string SelectedHexColor) ShowAddTagColorPicker(string initialHexColor, Window? owner = null)
    {
        var popup = new AddTagColorPickerPopup(initialHexColor);
        var dialogResult = ShowDialog(popup, owner);
        return (dialogResult, popup.SelectedHexColor);
    }

    public bool? ShowFeaturePlaceholder(string title, string message, Window? owner = null)
    {
        return ShowDialog(new FeaturePlaceholderPopup(title, message), owner);
    }

    public (bool? DialogResult, DeleteAllDataChoice Choice) ShowDeleteAllData(Window? owner = null)
    {
        var popup = new DeleteAllDataPopup();
        var dialogResult = ShowDialog(popup, owner);
        return (dialogResult, popup.Choice);
    }

    public Task ShowToastWhileAsync(string message, Func<Task> work, Window? owner = null)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Toast message cannot be empty.", nameof(message));

        ArgumentNullException.ThrowIfNull(work);

        var popup = new ToastPopup(message, work);
        ShowDialog(popup, owner);

        if (popup.ExecutionException is not null)
            ExceptionDispatchInfo.Capture(popup.ExecutionException).Throw();

        return Task.CompletedTask;
    }

    public Task ShowToastWhileAsync(string message, Action work, Window? owner = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        return ShowToastWhileAsync(message, () => Task.Run(work), owner);
    }

    public Task<string?> ShowDownloadUpdateAsync(
        AppUpdateCheckResult update,
        Func<IProgress<double>, CancellationToken, Task<string>> downloadInstallerAsync,
        Window? owner = null)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(downloadInstallerAsync);

        var popup = new DownloadUpdatePopup(update, downloadInstallerAsync);
        ShowDialog(popup, owner);

        popup.RethrowIfFailed();
        return Task.FromResult(popup.IsCanceled ? null : popup.InstallerPath);
    }

    public MessageBoxResult ShowWarning(string message, string title, Window? owner = null,
        MessageBoxButton buttons = MessageBoxButton.OK)
    {
        return _showMessageBox(owner, message, title, buttons, MessageBoxImage.Warning);
    }

    public MessageBoxResult ShowError(string message, string title, Window? owner = null,
        MessageBoxButton buttons = MessageBoxButton.OK)
    {
        return _showMessageBox(owner, message, title, buttons, MessageBoxImage.Error);
    }

    public MessageBoxResult ShowInformation(string message, string title, Window? owner = null,
        MessageBoxButton buttons = MessageBoxButton.OK)
    {
        return _showMessageBox(owner, message, title, buttons, MessageBoxImage.Information);
    }

    public MessageBoxResult ShowQuestion(string message, string title, Window? owner = null,
        MessageBoxButton buttons = MessageBoxButton.YesNo)
    {
        return _showMessageBox(owner, message, title, buttons, MessageBoxImage.Question);
    }

    private static bool? ShowDialog(Window popup, Window? owner)
    {
        var resolvedOwner = popup.Owner ?? ResolveOwner(owner);
        if (popup.Owner is null)
            popup.Owner = resolvedOwner;

        try
        {
            return popup.ShowDialog();
        }
        finally
        {
            ReactivateOwnerAfterDialogClose(resolvedOwner);
        }
    }

    private static void ReactivateOwnerAfterDialogClose(Window? owner)
    {
        if (owner is null || !owner.IsVisible || owner.WindowState == WindowState.Minimized)
            return;

        owner.Dispatcher.BeginInvoke(() =>
        {
            if (owner is null || !owner.IsVisible || owner.WindowState == WindowState.Minimized)
                return;

            owner.Activate();
            owner.Focus();
            Keyboard.Focus(owner);
        }, DispatcherPriority.ApplicationIdle);
    }

    private static Window? ResolveOwner(Window? owner)
    {
        if (owner is not null)
            return owner;

        var application = Application.Current;
        if (application is null)
            return null;

        var activeWindow = Enumerable.FirstOrDefault<Window>(
            application.Windows.OfType<Window>(),
            window => window.IsActive);
        return activeWindow ?? application.MainWindow;
    }

    private bool? ShowScopedDialog<TWindow>(Window? owner = null) where TWindow : Window
    {
        using var scope = _serviceProvider.CreateScope();
        var popup = scope.ServiceProvider.GetRequiredService<TWindow>();
        return ShowDialog(popup, owner);
    }
}
