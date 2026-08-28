using Fluxo.DataModels.Popups.GlobalSearch;
using MainWindowView = Fluxo.Views.Shell.Main.MainWindow;

namespace Fluxo.Helpers.MainWindow;

public static class MainWindowFeatureActivationHelper
{
    public static async Task ActivateAsync(MainWindowView window, GlobalSearchFeatureTarget target)
    {
        switch (target)
        {
            case GlobalSearchFeatureTarget.Dashboard:
            case GlobalSearchFeatureTarget.Analytics:
            case GlobalSearchFeatureTarget.Calendar:
            case GlobalSearchFeatureTarget.Ledger:
                await window.NavigateToFeaturePageAsync(target);
                break;
            case GlobalSearchFeatureTarget.QuickAccess:
                window.OpenQuickAddPopup();
                break;
            case GlobalSearchFeatureTarget.NewAccount:
                window.OpenAddAccountPopup();
                break;
            case GlobalSearchFeatureTarget.NewTransaction:
                window.OpenAddNewTransactionPopup();
                break;
            case GlobalSearchFeatureTarget.NewRecurringTransaction:
                window.OpenRecurringAddNewTransactionPopup();
                break;
            case GlobalSearchFeatureTarget.NewTag:
                window.OpenNewTagPopup();
                break;
            case GlobalSearchFeatureTarget.NewSavingGoal:
                window.OpenAddSavingGoalPopup();
                break;
            case GlobalSearchFeatureTarget.ViewAccounts:
                window.OpenAccountsListPopup();
                break;
            case GlobalSearchFeatureTarget.OpenSettings:
                window.OpenSettingsPopup();
                break;
            case GlobalSearchFeatureTarget.RunQuickSetup:
                window.OpenQuickSetupWizardPopup();
                break;
            case GlobalSearchFeatureTarget.CheckForUpdates:
                await window.CheckForUpdatesFromQuickAccessAsync();
                break;
            case GlobalSearchFeatureTarget.PlanningReport:
                window.OpenPlanningReport();
                break;
            case GlobalSearchFeatureTarget.BudgetForecast:
                window.OpenBudgetForecast();
                break;
            case GlobalSearchFeatureTarget.SearchEverything:
                await window.OpenGlobalSearchAsync();
                break;
            case GlobalSearchFeatureTarget.DataManagement:
                window.OpenDataManagementPopup();
                break;
            case GlobalSearchFeatureTarget.Hotkeys:
                window.OpenHotkeysOverviewPopup();
                break;
            case GlobalSearchFeatureTarget.LockApplication:
                window.LockAppUiFromUser();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, null);
        }
    }
}
