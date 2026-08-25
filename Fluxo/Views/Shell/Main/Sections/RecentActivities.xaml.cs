using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Fluxo.Core.Enums;
using Fluxo.Resources.Components;
using Fluxo.Resources.Infrastructure;

namespace Fluxo.Views.Shell.Main.Sections;

public partial class RecentActivities : UserControl
{
    public RecentActivities()
    {
        InitializeComponent();
    }

    private void OnTagListPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListView listView || e.OriginalSource is not DependencyObject source)
            return;

        var listViewItem = DependencyObjectTree.FindAncestor<ListViewItem>(source);
        if (listViewItem is null || !listViewItem.IsSelected)
            return;

        listView.SelectedItem = null;
        e.Handled = true;
    }

    private void OnMoreTagsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MoreTagsButton?.IsChecked != true)
            return;

        if (e.AddedItems.Count == 0 && e.RemovedItems.Count == 0)
            return;

        MoreTagsButton.IsChecked = false;
    }

    private void OnEmptyExpenseActionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not TransactionsList { EmptyActionParameter: ExpenseCategory category })
            return;

        if (Window.GetWindow(this) is not MainWindow mainWindow)
            return;

        mainWindow.OpenAddNewTransactionPopupForCategory(category);
    }

}
