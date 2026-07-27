using System.Windows.Input;
using Fluxo.Core.Enums;

namespace Fluxo.Helpers.MainWindow;

public static class MainWindowShortcutMatcher
{
    public static bool IsOpenPlanningShortcut(Key key, ModifierKeys modifiers)
    {
        return key == Key.P && modifiers == ModifierKeys.Control;
    }

    public static bool IsOpenBudgetForecastShortcut(Key key, ModifierKeys modifiers)
    {
        return key == Key.P && modifiers == (ModifierKeys.Control | ModifierKeys.Shift);
    }

    public static bool IsOpenAnalyticsShortcut(Key key, ModifierKeys modifiers)
    {
        return IsNumberKey(key, 2) && modifiers == ModifierKeys.Control;
    }

    public static bool IsOpenSearchShortcut(Key key, ModifierKeys modifiers)
    {
        return key == Key.F && modifiers == ModifierKeys.Control;
    }

    public static bool IsToggleHistoryShortcut(Key key, ModifierKeys modifiers)
    {
        return key == Key.H && modifiers == ModifierKeys.Control;
    }

    public static bool IsUndoShortcut(Key key, ModifierKeys modifiers)
    {
        return key == Key.Z && modifiers == ModifierKeys.Control;
    }

    public static bool IsRedoShortcut(Key key, ModifierKeys modifiers)
    {
        return key == Key.Y && modifiers == ModifierKeys.Control;
    }

    public static bool IsOpenNewTransactionShortcut(Key key, ModifierKeys modifiers)
    {
        return key == Key.N && modifiers == ModifierKeys.Control;
    }

    public static bool IsOpenQuickAccessShortcut(Key key, ModifierKeys modifiers)
    {
        return key == Key.K && modifiers == ModifierKeys.Control;
    }

    public static bool IsOpenHotkeysOverviewShortcut(Key key, ModifierKeys modifiers)
    {
        return (key is Key.OemQuestion or Key.Divide) && modifiers == ModifierKeys.Control;
    }

    public static bool IsOpenRecurringNewTransactionShortcut(Key key, ModifierKeys modifiers)
    {
        return key == Key.N && modifiers == (ModifierKeys.Control | ModifierKeys.Shift);
    }

    public static bool IsOpenSettingsShortcut(Key key, ModifierKeys modifiers)
    {
        return key == Key.OemComma && modifiers == ModifierKeys.Control;
    }

    public static bool IsOpenDashboardShortcut(Key key, ModifierKeys modifiers)
    {
        return IsNumberKey(key, 1) && modifiers == ModifierKeys.Control;
    }

    public static bool IsOpenCalendarShortcut(Key key, ModifierKeys modifiers)
    {
        return IsNumberKey(key, 3) && modifiers == ModifierKeys.Control;
    }

    public static bool IsOpenLedgerShortcut(Key key, ModifierKeys modifiers)
    {
        return IsNumberKey(key, 4) && modifiers == ModifierKeys.Control;
    }

    public static bool IsToggleNotificationsShortcut(Key key, ModifierKeys modifiers)
    {
        return key == Key.N && modifiers == (ModifierKeys.Control | ModifierKeys.Alt);
    }

    public static bool IsNavigateDashboardPreviousPeriodShortcut(Key key, ModifierKeys modifiers)
    {
        return key == Key.Left && modifiers == ModifierKeys.Control;
    }

    public static bool IsNavigateDashboardNextPeriodShortcut(Key key, ModifierKeys modifiers)
    {
        return key == Key.Right && modifiers == ModifierKeys.Control;
    }

    public static bool IsNavigateDashboardCurrentPeriodShortcut(Key key, ModifierKeys modifiers)
    {
        return key == Key.Home && modifiers == ModifierKeys.Control;
    }

    public static bool TryGetViewModeShortcut(Key key, ModifierKeys modifiers, out MainContentViewMode viewMode)
    {
        viewMode = MainContentViewMode.Daily;
        if (modifiers != (ModifierKeys.Control | ModifierKeys.Alt))
            return false;

        if (IsNumberKey(key, 1))
        {
            viewMode = MainContentViewMode.Daily;
            return true;
        }

        if (IsNumberKey(key, 2))
        {
            viewMode = MainContentViewMode.Weekly;
            return true;
        }

        if (IsNumberKey(key, 3))
        {
            viewMode = MainContentViewMode.Monthly;
            return true;
        }

        if (IsNumberKey(key, 4))
        {
            viewMode = MainContentViewMode.AllTime;
            return true;
        }

        return false;
    }

    public static bool IsOpenAddAccountShortcut(Key key, ModifierKeys modifiers)
    {
        _ = key;
        _ = modifiers;
        return false;
    }

    public static bool IsToggleAppLockShortcut(Key key, ModifierKeys modifiers)
    {
        return key == Key.L && modifiers == (ModifierKeys.Control | ModifierKeys.Shift);
    }

    public static bool IsOpenAddSavingGoalShortcut(Key key, ModifierKeys modifiers)
    {
        return key == Key.G && modifiers == (ModifierKeys.Control | ModifierKeys.Shift);
    }

    public static bool IsOpenDataManagementShortcut(Key key, ModifierKeys modifiers)
    {
        return key == Key.B && modifiers == (ModifierKeys.Control | ModifierKeys.Shift);
    }

    public static bool IsLedgerExportShortcut(Key key, ModifierKeys modifiers)
    {
        return key == Key.E && modifiers == ModifierKeys.Control;
    }

    public static bool IsLedgerClearFiltersShortcut(Key key, ModifierKeys modifiers)
    {
        return key == Key.R && modifiers == (ModifierKeys.Control | ModifierKeys.Shift);
    }

    public static bool IsLedgerAscendingSortShortcut(Key key, ModifierKeys modifiers)
    {
        return key == Key.Up && modifiers == ModifierKeys.Control;
    }

    public static bool IsLedgerDescendingSortShortcut(Key key, ModifierKeys modifiers)
    {
        return key == Key.Down && modifiers == ModifierKeys.Control;
    }

    private static bool IsNumberKey(Key key, int number)
    {
        return key == (Key)((int)Key.D0 + number) || key == (Key)((int)Key.NumPad0 + number);
    }
}
