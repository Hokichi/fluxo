using AutoMapper;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Core.Interfaces.Caching;
using Fluxo.DataModels.Messages;
using Fluxo.Mappings;
using Fluxo.Services.Backups;
using Fluxo.Services.Persistence;
using Fluxo.Services.Dialogs;
using Fluxo.Services.Notifications;
using Fluxo.Services.Ui;
using Fluxo.Services.Logging;
using Fluxo.Services.Theming;
using Fluxo.ViewModels.Controls;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Popups;
using Fluxo.ViewModels.Popups.DataManagement;
using Fluxo.ViewModels.Popups.Planning;
using Fluxo.ViewModels.Popups.Settings;
using Fluxo.Services.Updates;
using Fluxo.Services.Caching;
using Fluxo.ViewModels.Shell;
using Fluxo.Views.Popups;
using Fluxo.Views.Popups.Planning;
using Fluxo.Views.Popups.Settings;
using Fluxo.Views.Shell.Main;
using Fluxo.Views.Shell.Main.Pages;
using Fluxo.Views.Shell.Wizard;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using AllocationDataVM = Fluxo.ViewModels.Shell.Main.AllocationDataVM;
using AnalyticsVM = Fluxo.ViewModels.Shell.Main.AnalyticsVM;
using RecentActivitiesVM = Fluxo.ViewModels.Shell.Main.RecentActivitiesVM;
using CalendarVM = Fluxo.ViewModels.Shell.Main.CalendarVM;
using DashboardVM = Fluxo.ViewModels.Shell.Main.DashboardVM;
using DaySpinnerVM = Fluxo.ViewModels.Shell.Main.DaySpinnerVM;
using FloatingNotificationListVM = Fluxo.ViewModels.Shell.Main.FloatingNotificationListVM;
using LedgerVM = Fluxo.ViewModels.Shell.Main.LedgerVM;
using MainViewModeToggleVM = Fluxo.ViewModels.Shell.Main.MainViewModeToggleVM;
using MainVM = Fluxo.ViewModels.Shell.Main.MainVM;
using NotificationPanelVM = Fluxo.ViewModels.Shell.Main.NotificationPanelVM;
using SavingGoalsPanelVM = Fluxo.ViewModels.Shell.Main.SavingGoalsPanelVM;
using SpentAllowancePanelVM = Fluxo.ViewModels.Shell.Main.SpentAllowancePanelVM;
using UpcomingEventsPanelVM = Fluxo.ViewModels.Shell.Main.UpcomingEventsPanelVM;
using QuickSetupWizardVM = Fluxo.ViewModels.Shell.QuickSetupWizard.QuickSetupWizardVM;

namespace Fluxo.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFluxoPresentation(this IServiceCollection services)
    {
        var mapperConfig = new MapperConfiguration(cfg =>
        {
            cfg.AddProfile<EntityViewModelProfile>();
        }, NullLoggerFactory.Instance);

        services.AddSingleton<IMapper>(_ => mapperConfig.CreateMapper());

        services.AddTransient<ITransactionService, TransactionService>();
        services.AddTransient<IAnalyticsService, AnalyticsService>();
        services.AddTransient<ICalendarService, CalendarService>();
        services.AddSingleton<AppDataCacheHydrator>();
        services.AddSingleton<AppDataCache>();
        services.AddSingleton<IAppDataCache>(provider => provider.GetRequiredService<AppDataCache>());
        services.AddSingleton<AppDataCommitCoordinator>();
        services.AddScoped<IAppDataService, AppDataService>();
        services.AddTransient<IBudgetAllocationPeriodSyncService, BudgetAllocationPeriodSyncService>();
        services.AddTransient<IUserBackupService, UserBackupService>();
        services.AddTransient<StartupNotificationEvaluator>();

        return services;
    }

    public static IServiceCollection AddUIData(this IServiceCollection services)
    {
        services.AddSingleton<IMessenger>(_ => WeakReferenceMessenger.Default);
        services.AddScoped(_ => new TransactionPopupMessageToken(Guid.NewGuid()));
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<ILogService, FluxoLogService>();
        services.AddSingleton<IUiSettleAwaiter, UiSettleAwaiter>();
        services.AddSingleton<IStartupRegistrationService, StartupRegistrationService>();
        services.AddSingleton<IUiLockPasswordProtector, DpapiUiLockPasswordProtector>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<IAppUpdateService, AppUpdateService>();
        services.AddSingleton<IAppUpdateLifecycleService, AppUpdateLifecycleService>();
        services.AddSingleton<IAppUpdateInteractionService, AppUpdateInteractionService>();

        services.AddSingleton<MainVM>();
        services.AddSingleton<DashboardVM>();
        services.AddSingleton<DaySpinnerVM>();
        services.AddTransient<MainViewModeToggleVM>();
        services.AddSingleton<AllocationDataVM>();
        services.AddSingleton<RecentActivitiesVM>();
        services.AddSingleton<SpentAllowancePanelVM>();
        services.AddSingleton<NotificationPanelVM>();
        services.AddSingleton<SavingGoalsPanelVM>();
        services.AddSingleton<UpcomingEventsPanelVM>();
        services.AddSingleton<FloatingNotificationListVM>();
        services.AddSingleton<LedgerVM>();
        services.AddTransient<TransactionPopupVM>();
        services.AddTransient<TransactionBulkQueueVM>();
        services.AddTransient<TransactionSplitsVM>();
        services.AddTransient<AddAccountVM>();
        services.AddTransient<AddSavingGoalVM>();
        services.AddTransient<PlanningReportVM>();
        services.AddTransient<BudgetForecastVM>();
        services.AddTransient<QuickAccessVM>();
        services.AddTransient<AnalyticsVM>();
        services.AddTransient<CalendarVM>();
        services.AddTransient<SettingsBudgetTabVM>();
        services.AddTransient<SettingsAccountsTabVM>();
        services.AddTransient<SettingsRecurringTransactionsTabVM>();
        services.AddTransient<SettingsGoalsTabVM>();
        services.AddTransient<SettingsIoUsTabVM>();
        services.AddTransient<SettingsTagsTabVM>();
        services.AddTransient<SettingsPersonalizationTabVM>();
        services.AddTransient<DataManagementVM>();
        services.AddTransient<SettingsVM>();
        services.AddTransient<ViewModels.Shell.QuickSetupWizard.QuickSetupWizardGreetingPageVM>();
        services.AddTransient<ViewModels.Shell.QuickSetupWizard.QuickSetupWizardNamePageVM>();
        services.AddTransient<ViewModels.Shell.QuickSetupWizard.QuickSetupWizardMiddlePageVM>();
        services.AddTransient<ViewModels.Shell.QuickSetupWizard.QuickSetupWizardLoadingPageVM>();
        services.AddTransient<ViewModels.Shell.QuickSetupWizard.QuickSetupWizardFinalPageVM>();
        services.AddTransient<ViewModels.Shell.QuickSetupWizard.QuickSetupWizardAccountsVM>();
        services.AddTransient<ViewModels.Shell.QuickSetupWizard.QuickSetupWizardRecurringTransactionsVM>();
        services.AddTransient<ViewModels.Shell.QuickSetupWizard.QuickSetupWizardSavingGoalsVM>();
        services.AddTransient<ViewModels.Shell.QuickSetupWizard.QuickSetupWizardBudgetAllocationVM>();
        services.AddTransient<ViewModels.Shell.QuickSetupWizard.QuickSetupWizardPersonalizationVM>();
        services.AddTransient<ViewModels.Shell.QuickSetupWizard.QuickSetupWizardNotificationVM>();
        services.AddTransient<ViewModels.Shell.QuickSetupWizard.QuickSetupWizardSummaryVM>();
        services.AddTransient<QuickSetupWizardVM>();

        services.AddTransient<QuickAddPopup>();
        services.AddTransient<TransactionPopup>();
        services.AddTransient<HotkeysOverviewPopup>();
        services.AddTransient<AccountsListPopup>();
        services.AddTransient<AddAccountPopup>();
        services.AddTransient<AddSavingGoalPopup>();
        services.AddTransient<SettingsPopup>();
        services.AddTransient<DataManagementPopup>();
        services.AddTransient<QuickSetupWizard>();
        services.AddTransient<PlanningReportPopup>();
        services.AddTransient<BudgetForecastPopup>();
        services.AddTransient<Dashboard>();
        services.AddTransient<Analytics>();
        services.AddTransient<Calendar>();
        services.AddTransient<Ledger>();
        services.AddTransient<INotificationGroupingService, NotificationGroupingService>();

        services.AddSingleton<MainWindow>();
        services.AddSingleton<FloatingNotificationOverlayWindow>();

        return services;
    }
}
