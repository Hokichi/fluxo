using Fluxo.Resources.Components;
using Xunit;

namespace Fluxo.Tests.Views.Components;

public sealed class TransactionsListVisibleItemLimitTests
{
    [Theory]
    [InlineData(5)]
    [InlineData(8)]
    public void TransactionsListVisibleItemLimit_LimitItems_ReturnsRequestedPrefixWithoutMutatingSource(int limit)
    {
        var source = Enumerable.Range(1, 10).Cast<object>().ToList();

        var result = TransactionsList.LimitItems(source, limit).Cast<int>().ToList();

        Assert.Equal(Enumerable.Range(1, limit), result);
        Assert.Equal(10, source.Count);
    }
}
