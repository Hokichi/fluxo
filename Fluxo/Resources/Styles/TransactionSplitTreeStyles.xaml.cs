using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Fluxo.Resources.Styles;

public partial class TransactionSplitTreeStyles : ResourceDictionary
{
    private void OnSplitCardClick(object sender, RoutedEventArgs e)
    {
        TreeViewItem? item = null;
        TreeView? rootTree = null;
        for (var current = sender as DependencyObject; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            item ??= current as TreeViewItem;
            rootTree = current as TreeView ?? rootTree;
        }

        if (item is null)
            return;

        if (rootTree is not null)
            ClearTreeSelection(rootTree);
        item.IsSelected = true;
    }

    private void OnSplitCardMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        for (var current = sender as DependencyObject; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is TreeViewItem item)
            {
                item.IsExpanded = !item.IsExpanded;
                e.Handled = true;
                return;
            }
        }
    }

    private static void ClearTreeSelection(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is TreeViewItem item)
                item.IsSelected = false;
            ClearTreeSelection(child);
        }
    }
}
