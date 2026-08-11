using Fluxo.ViewModels.Popups;
using Xunit;

namespace Fluxo.Tests.ViewModels.Popups;

public sealed class AddNewTransactionHistoryListVMTests
{
    [Fact]
    public void AddNewTransactionHistoryListVM_Reset_LoadsFirstPageAndLoadMoreAppendsNextPage()
    {
        var list = new AddNewTransactionHistoryListVM(pageSize: 2);
        var items = Enumerable.Range(1, 5)
            .Select(id => new AddNewTransactionHistoryItemVM { Id = id, Name = $"Item {id}" })
            .ToList();

        list.Reset(items);

        Assert.Equal([1, 2], list.Items.Select(item => item.Id));
        Assert.True(list.HasMoreItems);

        list.LoadMoreCommand.Execute(null);

        Assert.Equal([1, 2, 3, 4], list.Items.Select(item => item.Id));
        Assert.True(list.HasMoreItems);

        list.LoadMoreCommand.Execute(null);

        Assert.Equal([1, 2, 3, 4, 5], list.Items.Select(item => item.Id));
        Assert.False(list.HasMoreItems);
    }
}
