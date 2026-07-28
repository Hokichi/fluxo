using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Fluxo.Resources.Styles;

public partial class TransactionSplitTreeStyles : ResourceDictionary
{
    public static void SyncSelection(TreeView tree, object? selectedItem)
    {
        foreach (var candidate in tree.Items)
            if (candidate is TreeViewItem directItem)
                directItem.IsSelected = false;

        ClearTreeSelection(tree);
        if (selectedItem is null)
            return;

        var item = FindTreeViewItem(tree, selectedItem);
        if (item is not null)
            item.IsSelected = true;
    }

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

    private static TreeViewItem? FindTreeViewItem(TreeView tree, object selectedItem)
    {
        foreach (var candidate in tree.Items)
            if (candidate is TreeViewItem item && ReferenceEquals(item.DataContext, selectedItem))
                return item;

        return FindTreeViewItem((DependencyObject)tree, selectedItem);
    }

    private static TreeViewItem? FindTreeViewItem(DependencyObject parent, object selectedItem)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is TreeViewItem item && ReferenceEquals(item.DataContext, selectedItem))
                return item;

            var match = FindTreeViewItem(child, selectedItem);
            if (match is not null)
                return match;
        }

        return null;
    }
}
