using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Fluxo.Resources.Styles;

public partial class TransactionSplitTreeStyles : ResourceDictionary
{
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
}
