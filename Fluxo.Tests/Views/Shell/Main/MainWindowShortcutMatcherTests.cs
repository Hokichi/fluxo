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
    public void IsUndoShortcut_MatchesOnlyCtrlZ(Key key, ModifierKeys modifiers, bool expected)
    {
        Assert.Equal(expected, MainWindowShortcutMatcher.IsUndoShortcut(key, modifiers));
    }

    [Theory]
    [InlineData(Key.Y, ModifierKeys.Control, true)]
    [InlineData(Key.Y, ModifierKeys.None, false)]
    [InlineData(Key.Y, ModifierKeys.Control | ModifierKeys.Shift, false)]
    [InlineData(Key.Z, ModifierKeys.Control, false)]
    public void IsRedoShortcut_MatchesOnlyCtrlY(Key key, ModifierKeys modifiers, bool expected)
    {
        Assert.Equal(expected, MainWindowShortcutMatcher.IsRedoShortcut(key, modifiers));
    }

    [Fact]
    public void IsToggleHistoryShortcut_ReturnsTrueOnlyForCtrlH()
    {
        Assert.True(MainWindowShortcutMatcher.IsToggleHistoryShortcut(Key.H, ModifierKeys.Control));
        Assert.False(MainWindowShortcutMatcher.IsToggleHistoryShortcut(Key.H, ModifierKeys.None));
        Assert.False(MainWindowShortcutMatcher.IsToggleHistoryShortcut(
            Key.H, ModifierKeys.Control | ModifierKeys.Shift));
    }

    [Fact]
    public void IsOpenNewTransactionShortcut_ReturnsTrue_ForCtrlN()
    {
        var isShortcut = MainWindowShortcutMatcher.IsOpenNewTransactionShortcut(Key.N, ModifierKeys.Control);

        Assert.True(isShortcut);
    }

    [Theory]
    [InlineData(Key.N, ModifierKeys.None)]
    [InlineData(Key.N, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(Key.M, ModifierKeys.Control)]
    public void IsOpenNewTransactionShortcut_ReturnsFalse_ForOtherKeysOrModifiers(Key key, ModifierKeys modifiers)
    {
        var isShortcut = MainWindowShortcutMatcher.IsOpenNewTransactionShortcut(key, modifiers);

        Assert.False(isShortcut);
    }

    [Fact]
    public void IsOpenPlanningShortcut_ReturnsTrue_ForCtrlP()
    {
        var isShortcut = MainWindowShortcutMatcher.IsOpenPlanningShortcut(Key.P, ModifierKeys.Control);

        Assert.True(isShortcut);
    }

    [Theory]
    [InlineData(Key.P, ModifierKeys.None)]
    [InlineData(Key.P, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(Key.O, ModifierKeys.Control)]
    public void IsOpenPlanningShortcut_ReturnsFalse_ForOtherKeysOrModifiers(Key key, ModifierKeys modifiers)
    {
        var isShortcut = MainWindowShortcutMatcher.IsOpenPlanningShortcut(key, modifiers);

        Assert.False(isShortcut);
    }

    [Fact]
    public void IsOpenBudgetForecastShortcut_ReturnsTrue_ForCtrlShiftP()
    {
        Assert.True(MainWindowShortcutMatcher.IsOpenBudgetForecastShortcut(
            Key.P,
            ModifierKeys.Control | ModifierKeys.Shift));
    }

    [Theory]
    [InlineData(Key.P, ModifierKeys.Control)]
    [InlineData(Key.P, ModifierKeys.None)]
    [InlineData(Key.O, ModifierKeys.Control | ModifierKeys.Shift)]
    public void IsOpenBudgetForecastShortcut_ReturnsFalse_ForOtherKeysOrModifiers(Key key, ModifierKeys modifiers)
    {
        Assert.False(MainWindowShortcutMatcher.IsOpenBudgetForecastShortcut(key, modifiers));
    }

    [Fact]
    public void IsOpenQuickAccessShortcut_ReturnsTrue_ForCtrlK()
    {
        var isShortcut = MainWindowShortcutMatcher.IsOpenQuickAccessShortcut(Key.K, ModifierKeys.Control);

        Assert.True(isShortcut);
    }

    [Theory]
    [InlineData(Key.Q, ModifierKeys.Control)]
    [InlineData(Key.Q, ModifierKeys.None)]
    [InlineData(Key.Q, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(Key.W, ModifierKeys.Control)]
    public void IsOpenQuickAccessShortcut_ReturnsFalse_ForOtherKeysOrModifiers(Key key, ModifierKeys modifiers)
    {
        var isShortcut = MainWindowShortcutMatcher.IsOpenQuickAccessShortcut(key, modifiers);

        Assert.False(isShortcut);
    }

    [Theory]
    [InlineData(Key.OemQuestion, ModifierKeys.Control)]
    [InlineData(Key.Divide, ModifierKeys.Control)]
    public void IsOpenHotkeysOverviewShortcut_ReturnsTrue_ForCtrlSlash(Key key, ModifierKeys modifiers)
    {
        var isShortcut = MainWindowShortcutMatcher.IsOpenHotkeysOverviewShortcut(key, modifiers);

        Assert.True(isShortcut);
    }

    [Theory]
    [InlineData(Key.OemQuestion, ModifierKeys.None)]
    [InlineData(Key.OemQuestion, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(Key.H, ModifierKeys.Control)]
    public void IsOpenHotkeysOverviewShortcut_ReturnsFalse_ForOtherKeysOrModifiers(Key key, ModifierKeys modifiers)
    {
        var isShortcut = MainWindowShortcutMatcher.IsOpenHotkeysOverviewShortcut(key, modifiers);

        Assert.False(isShortcut);
    }

    [Fact]
    public void IsOpenAnalyticsShortcut_ReturnsTrue_ForCtrl2()
    {
        var isShortcut = MainWindowShortcutMatcher.IsOpenAnalyticsShortcut(Key.D2, ModifierKeys.Control);

        Assert.True(isShortcut);
    }

    [Theory]
    [InlineData(Key.A, ModifierKeys.None)]
    [InlineData(Key.A, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(Key.D2, ModifierKeys.None)]
    [InlineData(Key.D, ModifierKeys.Control)]
    public void IsOpenAnalyticsShortcut_ReturnsFalse_ForOtherKeysOrModifiers(Key key, ModifierKeys modifiers)
    {
        var isShortcut = MainWindowShortcutMatcher.IsOpenAnalyticsShortcut(key, modifiers);

        Assert.False(isShortcut);
    }

    [Fact]
    public void IsOpenSearchShortcut_ReturnsTrue_ForCtrlF()
    {
        var isShortcut = MainWindowShortcutMatcher.IsOpenSearchShortcut(Key.F, ModifierKeys.Control);

        Assert.True(isShortcut);
    }

    [Theory]
    [InlineData(Key.F, ModifierKeys.None)]
    [InlineData(Key.F, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(Key.S, ModifierKeys.Control)]
    public void IsOpenSearchShortcut_ReturnsFalse_ForOtherKeysOrModifiers(Key key, ModifierKeys modifiers)
    {
        var isShortcut = MainWindowShortcutMatcher.IsOpenSearchShortcut(key, modifiers);

        Assert.False(isShortcut);
    }

    [Fact]
    public void IsOpenRecurringNewTransactionShortcut_ReturnsTrue_ForCtrlShiftN()
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
    public void IsOpenRecurringNewTransactionShortcut_ReturnsFalse_ForOtherKeysOrModifiers(Key key, ModifierKeys modifiers)
    {
        var isShortcut = MainWindowShortcutMatcher.IsOpenRecurringNewTransactionShortcut(key, modifiers);

        Assert.False(isShortcut);
    }

    [Theory]
    [InlineData(Key.OemComma, ModifierKeys.Control)]
    public void IsOpenSettingsShortcut_ReturnsTrue_ForCtrlComma(Key key, ModifierKeys modifiers)
    {
        Assert.True(MainWindowShortcutMatcher.IsOpenSettingsShortcut(key, modifiers));
    }

    [Theory]
    [InlineData(Key.D1, ModifierKeys.Control)]
    [InlineData(Key.NumPad1, ModifierKeys.Control)]
    public void IsOpenDashboardShortcut_ReturnsTrue_ForCtrl1(Key key, ModifierKeys modifiers)
    {
        Assert.True(MainWindowShortcutMatcher.IsOpenDashboardShortcut(key, modifiers));
    }

    [Theory]
    [InlineData(Key.D3, ModifierKeys.Control)]
    [InlineData(Key.NumPad3, ModifierKeys.Control)]
    public void IsOpenCalendarShortcut_ReturnsTrue_ForCtrl3(Key key, ModifierKeys modifiers)
    {
        Assert.True(MainWindowShortcutMatcher.IsOpenCalendarShortcut(key, modifiers));
    }

    [Theory]
    [InlineData(Key.D4, ModifierKeys.Control)]
    [InlineData(Key.NumPad4, ModifierKeys.Control)]
    public void IsOpenLedgerShortcut_ReturnsTrue_ForCtrl4(Key key, ModifierKeys modifiers)
    {
        Assert.True(MainWindowShortcutMatcher.IsOpenLedgerShortcut(key, modifiers));
    }

    [Theory]
    [InlineData(Key.N, ModifierKeys.Control | ModifierKeys.Alt)]
    public void IsToggleNotificationsShortcut_ReturnsTrue_ForCtrlAltN(Key key, ModifierKeys modifiers)
    {
        Assert.True(MainWindowShortcutMatcher.IsToggleNotificationsShortcut(key, modifiers));
    }

    [Theory]
    [InlineData(Key.Left, ModifierKeys.Control)]
    public void IsNavigateDashboardPreviousPeriodShortcut_ReturnsTrue_ForCtrlLeft(Key key, ModifierKeys modifiers)
    {
        Assert.True(MainWindowShortcutMatcher.IsNavigateDashboardPreviousPeriodShortcut(key, modifiers));
    }

    [Theory]
    [InlineData(Key.Right, ModifierKeys.Control)]
    public void IsNavigateDashboardNextPeriodShortcut_ReturnsTrue_ForCtrlRight(Key key, ModifierKeys modifiers)
    {
        Assert.True(MainWindowShortcutMatcher.IsNavigateDashboardNextPeriodShortcut(key, modifiers));
    }

    [Theory]
    [InlineData(Key.Home, ModifierKeys.Control)]
    public void IsNavigateDashboardCurrentPeriodShortcut_ReturnsTrue_ForCtrlHome(Key key, ModifierKeys modifiers)
    {
        Assert.True(MainWindowShortcutMatcher.IsNavigateDashboardCurrentPeriodShortcut(key, modifiers));
    }

    [Theory]
    [InlineData(Key.D1, ModifierKeys.Control | ModifierKeys.Alt)]
    [InlineData(Key.D2, ModifierKeys.Control | ModifierKeys.Alt)]
    [InlineData(Key.D3, ModifierKeys.Control | ModifierKeys.Alt)]
    [InlineData(Key.D4, ModifierKeys.Control | ModifierKeys.Alt)]
    public void TryGetViewModeShortcut_ReturnsTrue_ForCtrlAltNumber(Key key, ModifierKeys modifiers)
    {
        Assert.True(MainWindowShortcutMatcher.TryGetViewModeShortcut(key, modifiers, out _));
    }

    [Fact]
    public void TryGetViewModeShortcut_ReturnsFalse_ForAltOnlyNumber()
    {
        Assert.False(MainWindowShortcutMatcher.TryGetViewModeShortcut(Key.D1, ModifierKeys.Alt, out _));
    }

    [Theory]
    [InlineData(Key.L, ModifierKeys.Control | ModifierKeys.Shift)]
    public void IsOpenAddAccountShortcut_ReturnsFalse_ForCtrlShiftL(Key key, ModifierKeys modifiers)
    {
        Assert.False(MainWindowShortcutMatcher.IsOpenAddAccountShortcut(key, modifiers));
    }

    [Theory]
    [InlineData(Key.L, ModifierKeys.Control | ModifierKeys.Shift)]
    public void IsToggleAppLockShortcut_ReturnsTrue_ForCtrlShiftL(Key key, ModifierKeys modifiers)
    {
        Assert.True(MainWindowShortcutMatcher.IsToggleAppLockShortcut(key, modifiers));
    }

    [Theory]
    [InlineData(Key.G, ModifierKeys.Control | ModifierKeys.Shift)]
    public void IsOpenAddSavingGoalShortcut_ReturnsTrue_ForCtrlShiftG(Key key, ModifierKeys modifiers)
    {
        Assert.True(MainWindowShortcutMatcher.IsOpenAddSavingGoalShortcut(key, modifiers));
    }

    [Fact]
    public void IsOpenAddSavingGoalShortcut_ReturnsFalse_ForCtrlShiftE()
    {
        Assert.False(MainWindowShortcutMatcher.IsOpenAddSavingGoalShortcut(
            Key.E,
            ModifierKeys.Control | ModifierKeys.Shift));
    }

    [Theory]
    [InlineData(Key.E, ModifierKeys.Control)]
    public void IsLedgerExportShortcut_ReturnsTrue_ForCtrlE(Key key, ModifierKeys modifiers)
    {
        Assert.True(MainWindowShortcutMatcher.IsLedgerExportShortcut(key, modifiers));
    }

    [Theory]
    [InlineData(Key.R, ModifierKeys.Control | ModifierKeys.Shift)]
    public void IsLedgerClearFiltersShortcut_ReturnsTrue_ForCtrlShiftR(Key key, ModifierKeys modifiers)
    {
        Assert.True(MainWindowShortcutMatcher.IsLedgerClearFiltersShortcut(key, modifiers));
    }

    [Theory]
    [InlineData(Key.Up, ModifierKeys.Control)]
    public void IsLedgerAscendingSortShortcut_ReturnsTrue_ForCtrlUp(Key key, ModifierKeys modifiers)
    {
        Assert.True(MainWindowShortcutMatcher.IsLedgerAscendingSortShortcut(key, modifiers));
    }

    [Theory]
    [InlineData(Key.Down, ModifierKeys.Control)]
    public void IsLedgerDescendingSortShortcut_ReturnsTrue_ForCtrlDown(Key key, ModifierKeys modifiers)
    {
        Assert.True(MainWindowShortcutMatcher.IsLedgerDescendingSortShortcut(key, modifiers));
    }

    [Theory]
    [InlineData(Key.B, ModifierKeys.Control | ModifierKeys.Shift)]
    public void IsOpenDataManagementShortcut_ReturnsTrue_ForCtrlShiftB(Key key, ModifierKeys modifiers)
    {
        Assert.True(MainWindowShortcutMatcher.IsOpenDataManagementShortcut(key, modifiers));
    }

    [Fact]
    public void RemovedShortcuts_ReturnFalse()
    {
        Assert.False(MainWindowShortcutMatcher.IsOpenAnalyticsShortcut(
            Key.A,
            ModifierKeys.Control | ModifierKeys.Shift));
        Assert.False(MainWindowShortcutMatcher.IsOpenSettingsShortcut(Key.S, ModifierKeys.Control));
    }
}
