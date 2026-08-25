using Fluxo.Core.Interfaces.Services;
using Fluxo.Data.Enums;
using Fluxo.DataModels.Popups.GlobalSearch;

namespace Fluxo.Helpers.MainWindow;

public sealed class GlobalSearchCandidateLoader(IAppDataService appData)
{
    public async Task<IReadOnlyList<GlobalSearchResult>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var transactionsTask = appData.GetTransactionsAsync(cancellationToken);
        var accountsTask = appData.GetAccountsAsync(cancellationToken);
        var tagsTask = appData.GetTagsAsync(cancellationToken);
        var goalsTask = appData.GetSavingGoalsAsync(cancellationToken);

        await Task.WhenAll(transactionsTask, accountsTask, tagsTask, goalsTask).ConfigureAwait(false);

        var candidates = new List<GlobalSearchResult>();
        candidates.AddRange((await transactionsTask.ConfigureAwait(false))
            .Where(transaction => !transaction.IsForDeletion)
            .Select(transaction => new GlobalSearchResult(
                GlobalSearchResultType.Transactions,
                transaction.Name,
                EntityId: transaction.Id,
                LoggedOn: transaction.LoggedOn)));
        candidates.AddRange((await accountsTask.ConfigureAwait(false))
            .Where(account => !account.IsForDeletion)
            .Select(account => new GlobalSearchResult(
                GlobalSearchResultType.Accounts,
                account.Name,
                EntityId: account.Id)));
        candidates.AddRange((await tagsTask.ConfigureAwait(false))
            .Select(tag => new GlobalSearchResult(GlobalSearchResultType.Tags, tag.Name, EntityId: tag.Id)));
        candidates.AddRange((await goalsTask.ConfigureAwait(false))
            .Select(goal => new GlobalSearchResult(GlobalSearchResultType.Goals, goal.Name, EntityId: goal.Id)));
        candidates.AddRange(FeatureCandidates);
        candidates.AddRange(SettingsCandidates);
        return candidates;
    }

    private static readonly IReadOnlyList<GlobalSearchResult> FeatureCandidates =
    [
        Feature("Dashboard", GlobalSearchFeatureTarget.Dashboard),
        Feature("Analytics", GlobalSearchFeatureTarget.Analytics),
        Feature("Calendar", GlobalSearchFeatureTarget.Calendar),
        Feature("Ledger", GlobalSearchFeatureTarget.Ledger),
        Feature("Quick access", GlobalSearchFeatureTarget.QuickAccess),
        Feature("New account", GlobalSearchFeatureTarget.NewAccount),
        Feature("New transaction", GlobalSearchFeatureTarget.NewTransaction),
        Feature("New tag", GlobalSearchFeatureTarget.NewTag),
        Feature("New saving goal", GlobalSearchFeatureTarget.NewSavingGoal),
        Feature("View accounts", GlobalSearchFeatureTarget.ViewAccounts),
        Feature("Open settings", GlobalSearchFeatureTarget.OpenSettings),
        Feature("Run quick setup", GlobalSearchFeatureTarget.RunQuickSetup),
        Feature("Check for updates", GlobalSearchFeatureTarget.CheckForUpdates),
        Feature("Planning report", GlobalSearchFeatureTarget.PlanningReport),
        Feature("Budget forecast", GlobalSearchFeatureTarget.BudgetForecast),
        Feature("Data management", GlobalSearchFeatureTarget.DataManagement),
        Feature("Hotkeys", GlobalSearchFeatureTarget.Hotkeys),
        Feature("Lock application", GlobalSearchFeatureTarget.LockApplication)
    ];

    private static readonly IReadOnlyList<GlobalSearchResult> SettingsCandidates =
    [
        Budget("Budget Management", "Settings.Budget.Root", SettingsBudgetManagementPage.Allocation),
        Budget("Needs", "Settings.Budget.Needs", SettingsBudgetManagementPage.Allocation),
        Budget("Wants", "Settings.Budget.Wants", SettingsBudgetManagementPage.Allocation),
        Budget("Invest", "Settings.Budget.Invest", SettingsBudgetManagementPage.Allocation),
        Budget("Allocation Period", "Settings.Budget.AllocationPeriod", SettingsBudgetManagementPage.Configuration),
        Budget("Period Start", "Settings.Budget.PeriodStart", SettingsBudgetManagementPage.Configuration),
        Budget("Spending Limit", "Settings.Budget.SpendingLimit", SettingsBudgetManagementPage.Configuration),
        Budget("Budget Rollover", "Settings.Budget.Rollover", SettingsBudgetManagementPage.Configuration),
        Budget("Overspend Behavior", "Settings.Budget.Overspend", SettingsBudgetManagementPage.Configuration),
        Setting("Preferences", SettingsSearchSection.Personalization, "Settings.Personalization.Root"),
        Setting("Username", SettingsSearchSection.Personalization, "Settings.Personalization.Username"),
        Setting("Lock UI when away", SettingsSearchSection.Personalization, "Settings.Personalization.AutoLock"),
        Setting("Auto-lock after", SettingsSearchSection.Personalization, "Settings.Personalization.AutoLockInterval"),
        Setting("Unlock password", SettingsSearchSection.Personalization, "Settings.Personalization.Password"),
        Setting("Data Backup/Restore", SettingsSearchSection.Personalization, "Settings.Personalization.DataManagement"),
        Setting("Run Setup Wizard", SettingsSearchSection.Personalization, "Settings.Personalization.SetupWizard"),
        Setting("Configuration", SettingsSearchSection.Configuration, "Settings.Configuration.Root"),
        Setting("Check for updates", SettingsSearchSection.Configuration, "Settings.Configuration.CheckUpdates"),
        Setting("Run with Windows", SettingsSearchSection.Configuration, "Settings.Configuration.RunWithWindows"),
        Setting("When closing Fluxo", SettingsSearchSection.Configuration, "Settings.Configuration.CloseBehavior"),
        Setting("Minimize to tray", SettingsSearchSection.Configuration, "Settings.Configuration.MinimizeToTray"),
        Setting("Exit", SettingsSearchSection.Configuration, "Settings.Configuration.Exit"),
        Setting("Reset All Settings", SettingsSearchSection.Configuration, "Settings.Configuration.Reset"),
        Setting("Delete All Data", SettingsSearchSection.Configuration, "Settings.Configuration.DeleteAllData")
    ];

    private static GlobalSearchResult Feature(string name, GlobalSearchFeatureTarget target) =>
        new(GlobalSearchResultType.Features, name, "Feature", FeatureTarget: target);

    private static GlobalSearchResult Budget(string name, string automationId, SettingsBudgetManagementPage page) =>
        new(
            GlobalSearchResultType.Settings,
            name,
            "Budget",
            SettingsTarget: new SettingsSearchTarget(SettingsSearchSection.Budget, automationId, page));

    private static GlobalSearchResult Setting(string name, SettingsSearchSection section, string automationId) =>
        new(
            GlobalSearchResultType.Settings,
            name,
            section.ToString(),
            SettingsTarget: new SettingsSearchTarget(section, automationId));
}
