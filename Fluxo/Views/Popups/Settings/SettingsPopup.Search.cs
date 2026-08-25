using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Fluxo.DataModels.Popups.GlobalSearch;
using Fluxo.Resources.Infrastructure;

namespace Fluxo.Views.Popups.Settings;

public partial class SettingsPopup
{
    private SettingsSearchTarget? _searchTarget;

    internal void ConfigureSearchTarget(SettingsSearchTarget target)
    {
        _searchTarget = target ?? throw new ArgumentNullException(nameof(target));
    }

    internal async Task<bool> ApplySearchTargetAsync(SettingsSearchTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var tabButton = target.Section switch
        {
            SettingsSearchSection.Budget => BudgetTabButton,
            SettingsSearchSection.Personalization => PreferencesTabButton,
            SettingsSearchSection.Configuration => AboutTabButton,
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };

        if (target.BudgetPage is not null)
            _viewModel.BudgetTab.SelectedBudgetManagementPage = target.BudgetPage.Value;
        if (target.Section == SettingsSearchSection.Personalization)
            _viewModel.PersonalizationTab.SelectedPreferencesPage = "Personalization";

        if (!tabButton.IsChecked.GetValueOrDefault())
            await CrossfadeSettingsTabAsync(tabButton);

        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        var content = GetContentForTab(tabButton);
        var targetElement = content is null ? null : FindSearchTarget(content, target.AutomationId);
        if (targetElement is null)
            return false;

        targetElement.BringIntoView();
        return targetElement is UIElement { Focusable: true, IsEnabled: true, IsVisible: true } element
            ? element.Focus()
            : FocusFirstFocusableDescendant(targetElement);
    }

    internal static FrameworkElement? FindSearchTarget(DependencyObject root, string automationId)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(automationId);

        if (root is FrameworkElement element &&
            string.Equals(AutomationProperties.GetAutomationId(element), automationId, StringComparison.Ordinal))
        {
            return element;
        }

        foreach (var child in DependencyObjectTree.GetChildren(root))
        {
            var target = FindSearchTarget(child, automationId);
            if (target is not null)
                return target;
        }

        return null;
    }
}
