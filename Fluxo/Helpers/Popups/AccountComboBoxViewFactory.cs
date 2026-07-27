using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;

namespace Fluxo.Helpers.Popups;

public static class AccountComboBoxViewFactory
{
    public static ListCollectionView CreateGroupedByTypeThenName<T>(
        ObservableCollection<T> items,
        string groupPropertyName,
        string typeSortPropertyName,
        string nameSortPropertyName)
    {
        var view = (ListCollectionView)CollectionViewSource.GetDefaultView(items);

        view.GroupDescriptions.Clear();
        view.SortDescriptions.Clear();

        view.GroupDescriptions.Add(new PropertyGroupDescription(groupPropertyName));
        view.SortDescriptions.Add(new SortDescription(typeSortPropertyName, ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameSortPropertyName, ListSortDirection.Ascending));

        return view;
    }

    public static ListCollectionView CreateGroupedByProperty<T>(
        ObservableCollection<T> items,
        string groupPropertyName)
    {
        var view = (ListCollectionView)CollectionViewSource.GetDefaultView(items);

        view.GroupDescriptions.Clear();
        view.SortDescriptions.Clear();
        view.GroupDescriptions.Add(new PropertyGroupDescription(groupPropertyName));

        return view;
    }
}
