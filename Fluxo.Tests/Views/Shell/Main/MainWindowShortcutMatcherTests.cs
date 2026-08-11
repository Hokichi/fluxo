using System.Windows.Input;
using Fluxo.Views.Shell.Main;
using Fluxo.Helpers.MainWindow;
using Xunit;

namespace Fluxo.Tests.Views.Shell.Main;

public class MainWindowShortcutMatcherTests
{
    [Theory]
    [InlineData(Key.Z, ModifierKeys.Control, true)]
    [InlineData(Key.Z, ModifierKeys.None, false)]
    [InlineData(Key.Z, ModifierKeys.Control | ModifierKeys.Shift, false)]
    [InlineData(Key.Y, ModifierKeys.Control, false)]
    public void MainWindowShortcutMatcher_IsUndoShortcut_MatchesOnlyCtrlZ(Key key, ModifierKeys modifiers, bool expected)
    {
        Assert.Equal(expected, MainWindowShortcutMatcher.IsUndoShortcut(key, modifiers));
    }

    [Theory]
    [InlineData(Key.Y, ModifierKeys.Control, true)]
    [InlineData(Key.Y, ModifierKeys.None, false)]
    [InlineData(Key.Y, ModifierKeys.Control | ModifierKeys.Shift, false)]
    [InlineData(Key.Z, ModifierKeys.Control, false)]
    public void MainWindowShortcutMatcher_IsRedoShortcut_MatchesOnlyCtrlY(Key key, ModifierKeys modifiers, bool expected)
    {
        Assert.Equal(expected, MainWindowShortcutMatcher.IsRedoShortcut(key, modifiers));
    }

    [Fact]
    public void MainWindowShortcutMatcher_IsToggleHistoryShortcut_ReturnsTrueOnlyForCtrlH()
    {
        Assert.True(MainWindowShortcutMatcher.IsToggleHistoryShortcut(Key.H, ModifierKeys.Control));
        Assert.False(MainWindowShortcutMatcher.IsToggleHistoryShortcut(Key.H, ModifierKeys.None));
        Assert.False(MainWindowShortcutMatcher.IsToggleHistoryShortcut(
            Key.H, ModifierKeys.Control | ModifierKeys.Shift));
    }

    [Fact]
    public void MainWindowShortcutMatcher_IsOpenNewTransactionShortcut_ReturnsTrue_ForCtrlN()
    {
        var isShortcut = MainWindowShortcutMatcher.IsOpenNewTransactionShortcut(Key.N, ModifierKeys.Control);

        Assert.True(isShortcut);
    }

    [Theory]
    [InlineData(Key.N, ModifierKeys.None)]
    [InlineData(Key.N, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(Key.M, ModifierKeys.Control)]
    public void MainWindowShortcutMatcher_IsOpenNewTransactionShortcut_ReturnsFalseForOtherKeysOrModifiers(Key key, ModifierKeys modifiers)
    {
        var isShortcut = MainWindowShortcutMatcher.IsOpenNewTransactionShortcut(key, modifiers);

        Assert.False(isShortcut);
    }

    [Fact]
    public void MainWindowShortcutMatcher_IsOpenPlanningShortcut_ReturnsTrue_ForCtrlP()
    {
        var isShortcut = MainWindowShortcutMatcher.IsOpenPlanningShortcut(Key.P, ModifierKeys.Control);

        Assert.True(isShortcut);
    }

    [Theory]
    [InlineData(Key.P, ModifierKeys.None)]
    [InlineData(Key.P, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(Key.O, ModifierKeys.Control)]
    public void MainWindowShortcutMatcher_IsOpenPlanningShortcut_ReturnsFalseForOtherKeysOrModifiers(Key key, ModifierKeys modifiers)
    {
        var isShortcut = MainWindowShortcutMatcher.IsOpenPlanningShortcut(key, modifiers);

        Assert.False(isShortcut);
    }

    [Fact]
    public void MainWindowShortcutMatcher_IsOpenBudgetForecastShortcut_ReturnsTrue_ForCtrlShiftP()
    {
        Assert.True(MainWindowShortcutMatcher.IsOpenBudgetForecastShortcut(
            Key.P,
            ModifierKeys.Control | ModifierKeys.Shift));
    }

    [Theory]
    [InlineData(Key.P, ModifierKeys.Control)]
    [InlineData(Key.P, ModifierKeys.None)]
    [InlineData(Key.O, ModifierKeys.Control | ModifierKeys.Shift)]
    public void MainWindowShortcutMatcher_IsOpenBudgetForecastShortcut_ReturnsFalseForOtherKeysOrModifiers(Key key, ModifierKeys modifiers)
    {
        Assert.False(MainWindowShortcutMatcher.IsOpenBudgetForecastShortcut(key, modifiers));
    }

    [Fact]
    public void MainWindowShortcutMatcher_IsOpenQuickAccessShortcut_ReturnsTrue_ForCtrlK()
    {
        var isShortcut = MainWindowShortcutMatcher.IsOpenQuickAccessShortcut(Key.K, ModifierKeys.Control);

        Assert.True(isShortcut);
    }

    [Theory]
    [InlineData(Key.Q, ModifierKeys.Control)]
    [InlineData(Key.Q, ModifierKeys.None)]
    [InlineData(Key.Q, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(Key.W, ModifierKeys.Control)]
    public void MainWindowShortcutMatcher_IsOpenQuickAccessShortcut_ReturnsFalseForOtherKeysOrModifiers(Key key, ModifierKeys modifiers)
    {
        var isShortcut = MainWindowShortcutMatcher.IsOpenQuickAccessShortcut(key, modifiers);

        Assert.False(isShortcut);
    }

    [Theory]
    [InlineData(Key.OemQuestion, ModifierKeys.Control)]
    [InlineData(Key.Divide, ModifierKeys.Control)]
    public void MainWindowShortcutMatcher_IsOpenHotkeysOverviewShortcut_ReturnsTrueForCtrlSlash(Key key, ModifierKeys modifiers)
    {
        var isShortcut = MainWindowShortcutMatcher.IsOpenHotkeysOverviewShortcut(key, modifiers);

        Assert.True(isShortcut);
    }

    [Theory]
    [InlineData(Key.OemQuestion, ModifierKeys.None)]
    [InlineData(Key.OemQuestion, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(Key.H, ModifierKeys.Control)]
    public void MainWindowShortcutMatcher_IsOpenHotkeysOverviewShortcut_ReturnsFalseForOtherKeysOrModifiers(Key key, ModifierKeys modifiers)
    {
        var isShortcut = MainWindowShortcutMatcher.IsOpenHotkeysOverviewShortcut(key, modifiers);

        Assert.False(isShortcut);
    }

    [Fact]
    public void MainWindowShortcutMatcher_IsOpenAnalyticsShortcut_ReturnsTrue_ForCtrl2()
    {
        var isShortcut = MainWindowShortcutMatcher.IsOpenAnalyticsShortcut(Key.D2, ModifierKeys.Control);

        Assert.True(isShortcut);
    }

    [Theory]
    [InlineData(Key.A, ModifierKeys.None)]
    [InlineData(Key.A, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(Key.D2, ModifierKeys.None)]
    [InlineData(Key.D, ModifierKeys.Control)]
    public void MainWindowShortcutMatcher_IsOpenAnalyticsShortcut_ReturnsFalseForOtherKeysOrModifiers(Key key, ModifierKeys modifiers)
    {
        var isShortcut = MainWindowShortcutMatcher.IsOpenAnalyticsShortcut(key, modifiers);

        Assert.False(isShortcut);
    }

    [Fact]
    public void MainWindowShortcutMatcher_IsOpenSearchShortcut_ReturnsTrue_ForCtrlF()
    {
        var isShortcut = MainWindowShortcutMatcher.IsOpenSearchShortcut(Key.F, ModifierKeys.Control);

        Assert.True(isShortcut);
    }

    [Theory]
    [InlineData(Key.F, ModifierKeys.None)]
    [InlineData(Key.F, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(Key.S, ModifierKeys.Control)]
    public void MainWindowShortcutMatcher_IsOpenSearchShortcut_ReturnsFalseForOtherKeysOrModifiers(Key key, ModifierKeys modifiers)
    {
        var isShortcut = MainWindowShortcutMatcher.IsOpenSearchShortcut(key, modifiers);

        Assert.False(isShortcut);
    }

    [Fact]
    public void MainWindowShortcutMatcher_IsOpenRecurringNewTransactionShortcut_ReturnsTrue_ForCtrlShiftN()
    {
        var isShortcut = MainWindowShortcutMatcher.IsOpenRecurringNewTransactionShortcut(
            Key.N,
            ModifierKeys.Control | ModifierKeys.Shift);

        Assert.True(isShortcut);
    }

    [Theory]
    [InlineData(Key.N, ModifierKeys.None)]
    [InlineData(Key.N, ModifierKeys.Control)]
    [InlineData(Key.M, ModifierKeys.Control)]
    public void MainWindowShortcutMatcher_IsOpenRecurringNewTransactionShortcut_ReturnsFalseForOtherKeysOrModifiers(Key key, ModifierKeys modifiers)
    {
        var isShortcut = MainWindowShortcutMatcher.IsOpenRecurringNewTransactionShortcut(key, modifiers);

        Assert.False(isShortcut);
    }

    [Theory]
    [InlineData(Key.OemComma, ModifierKeys.Control)]
    public void MainWindowShortcutMatcher_IsOpenSettingsShortcut_ReturnsTrueForCtrlComma(Key key, ModifierKeys modifiers)
    {
        Assert.True(MainWindowShortcutMatcher.IsOpenSettingsShortcut(key, modifiers));
    }

    [Theory]
    [InlineData(Key.D1, ModifierKeys.Control)]
    [InlineData(Key.NumPad1, ModifierKeys.Control)]
    public void MainWindowShortcutMatcher_IsOpenDashboardShortcut_ReturnsTrueForCtrl1(Key key, ModifierKeys modifiers)
    {
        Assert.True(MainWindowShortcutMatcher.IsOpenDashboardShortcut(key, modifiers));
    }

    [Theory]
    [InlineData(Key.D3, ModifierKeys.Control)]
    [InlineData(Key.NumPad3, ModifierKeys.Control)]
    public void MainWindowShortcutMatcher_IsOpenCalendarShortcut_ReturnsTrueForCtrl3(Key key, ModifierKeys modifiers)
    {
        Assert.True(MainWindowShortcutMatcher.IsOpenCalendarShortcut(key, modifiers));
    }

    [Theory]
    [InlineData(Key.D4, ModifierKeys.Control)]
    [InlineData(Key.NumPad4, ModifierKeys.Control)]
    public void MainWindowShortcutMatcher_IsOpenLedgerShortcut_ReturnsTrueForCtrl4(Key key, ModifierKeys modifiers)
    {
        Assert.True(MainWindowShortcutMatcher.IsOpenLedgerShortcut(key, modifiers));
    }

    [Theory]
    [InlineData(Key.N, ModifierKeys.Control | ModifierKeys.Alt)]
    public void MainWindowShortcutMatcher_IsToggleNotificationsShortcut_ReturnsTrueForCtrlAltN(Key key, ModifierKeys modifiers)
    {
        Assert.True(MainWindowShortcutMatcher.IsToggleNotificationsShortcut(key, modifiers));
    }

    [Theory]
    [InlineData(Key.Left, ModifierKeys.Control)]
    public void MainWindowShortcutMatcher_IsNavigateDashboardPreviousPeriodShortcut_ReturnsTrueForCtrlLeft(Key key, ModifierKeys modifiers)
    {
        Assert.True(MainWindowShortcutMatcher.IsNavigateDashboardPreviousPeriodShortcut(key, modifiers));
    }

    [Theory]
    [InlineData(Key.Right, ModifierKeys.Control)]
    public void MainWindowShortcutMatcher_IsNavigateDashboardNextPeriodShortcut_ReturnsTrueForCtrlRight(Key key, ModifierKeys modifiers)
    {
        Assert.True(MainWindowShortcutMatcher.IsNavigateDashboardNextPeriodShortcut(key, modifiers));
    }

    [Theory]
    [InlineData(Key.Home, ModifierKeys.Control)]
    public void MainWindowShortcutMatcher_IsNavigateDashboardCurrentPeriodShortcut_ReturnsTrueForCtrlHome(Key key, ModifierKeys modifiers)
    {
        Assert.True(MainWindowShortcutMatcher.IsNavigateDashboardCurrentPeriodShortcut(key, modifiers));
    }

    [Theory]
    [InlineData(Key.D1, ModifierKeys.Control | ModifierKeys.Alt)]
    [InlineData(Key.D2, ModifierKeys.Control | ModifierKeys.Alt)]
    [InlineData(Key.D3, ModifierKeys.Control | ModifierKeys.Alt)]
    [InlineData(Key.D4, ModifierKeys.Control | ModifierKeys.Alt)]
    public void MainWindowShortcutMatcher_TryGetViewModeShortcut_ReturnsTrueForCtrlAltNumber(Key key, ModifierKeys modifiers)
    {
        Assert.True(MainWindowShortcutMatcher.TryGetViewModeShortcut(key, modifiers, out _));
    }

    [Fact]
    public void MainWindowShortcutMatcher_TryGetViewModeShortcut_ReturnsFalse_ForAltOnlyNumber()
    {
        Assert.False(MainWindowShortcutMatcher.TryGetViewModeShortcut(Key.D1, ModifierKeys.Alt, out _));
    }

    [Theory]
    [InlineData(Key.L, ModifierKeys.Control | ModifierKeys.Shift)]
    public void MainWindowShortcutMatcher_IsOpenAddAccountShortcut_ReturnsFalseForCtrlShiftL(Key key, ModifierKeys modifiers)
    {
        Assert.False(MainWindowShortcutMatcher.IsOpenAddAccountShortcut(key, modifiers));
    }

    [Theory]
    [InlineData(Key.L, ModifierKeys.Control | ModifierKeys.Shift)]
    public void MainWindowShortcutMatcher_IsToggleAppLockShortcut_ReturnsTrueForCtrlShiftL(Key key, ModifierKeys modifiers)
    {
        Assert.True(MainWindowShortcutMatcher.IsToggleAppLockShortcut(key, modifiers));
    }

    [Theory]
    [InlineData(Key.G, ModifierKeys.Control | ModifierKeys.Shift)]
    public void MainWindowShortcutMatcher_IsOpenAddSavingGoalShortcut_ReturnsTrueForCtrlShiftG(Key key, ModifierKeys modifiers)
    {
        Assert.True(MainWindowShortcutMatcher.IsOpenAddSavingGoalShortcut(key, modifiers));
    }

    [Fact]
    public void MainWindowShortcutMatcher_IsOpenAddSavingGoalShortcut_ReturnsFalse_ForCtrlShiftE()
    {
        Assert.False(MainWindowShortcutMatcher.IsOpenAddSavingGoalShortcut(
            Key.E,
            ModifierKeys.Control | ModifierKeys.Shift));
    }

    [Theory]
    [InlineData(Key.E, ModifierKeys.Control)]
    public void MainWindowShortcutMatcher_IsLedgerExportShortcut_ReturnsTrueForCtrlE(Key key, ModifierKeys modifiers)
    {
        Assert.True(MainWindowShortcutMatcher.IsLedgerExportShortcut(key, modifiers));
    }

    [Theory]
    [InlineData(Key.R, ModifierKeys.Control | ModifierKeys.Shift)]
    public void MainWindowShortcutMatcher_IsLedgerClearFiltersShortcut_ReturnsTrueForCtrlShiftR(Key key, ModifierKeys modifiers)
    {
        Assert.True(MainWindowShortcutMatcher.IsLedgerClearFiltersShortcut(key, modifiers));
    }

    [Theory]
    [InlineData(Key.Up, ModifierKeys.Control)]
    public void MainWindowShortcutMatcher_IsLedgerAscendingSortShortcut_ReturnsTrueForCtrlUp(Key key, ModifierKeys modifiers)
    {
        Assert.True(MainWindowShortcutMatcher.IsLedgerAscendingSortShortcut(key, modifiers));
    }

    [Theory]
    [InlineData(Key.Down, ModifierKeys.Control)]
    public void MainWindowShortcutMatcher_IsLedgerDescendingSortShortcut_ReturnsTrueForCtrlDown(Key key, ModifierKeys modifiers)
    {
        Assert.True(MainWindowShortcutMatcher.IsLedgerDescendingSortShortcut(key, modifiers));
    }

    [Theory]
    [InlineData(Key.B, ModifierKeys.Control | ModifierKeys.Shift)]
    public void MainWindowShortcutMatcher_IsOpenDataManagementShortcut_ReturnsTrueForCtrlShiftB(Key key, ModifierKeys modifiers)
    {
        Assert.True(MainWindowShortcutMatcher.IsOpenDataManagementShortcut(key, modifiers));
    }

    [Fact]
    public void MainWindowShortcutMatcher_RemovedShortcuts_ReturnFalse()
    {
        Assert.False(MainWindowShortcutMatcher.IsOpenAnalyticsShortcut(
            Key.A,
            ModifierKeys.Control | ModifierKeys.Shift));
        Assert.False(MainWindowShortcutMatcher.IsOpenSettingsShortcut(Key.S, ModifierKeys.Control));
    }
}
