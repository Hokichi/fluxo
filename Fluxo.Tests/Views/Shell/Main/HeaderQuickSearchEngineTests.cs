using Fluxo.Core.Enums;
using Fluxo.ViewModels.Entities;
using Fluxo.Views.Shell.Main;
using Fluxo.Helpers.MainWindow;
using Xunit;

namespace Fluxo.Tests.Views.Shell.Main;

public class HeaderQuickSearchEngineTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    public void HeaderQuickSearchEngine_Search_ReturnsEmptyForShortQuery(string? query)
    {
        Assert.Empty(HeaderQuickSearchEngine.Search([Create(1, "Groceries")], query));
    }

    [Fact]
    public void HeaderQuickSearchEngine_Search_SearchesBothTypes_AndOrdersByOccurredThenLogged()
    {
        var today = DateTime.Today;
        var transactions = new[]
        {
            Create(1, "Shared older", TransactionType.Expense, today.AddDays(-1), today),
            Create(2, "Shared first", TransactionType.Income, today, today.AddHours(1)),
            Create(3, "Shared latest", TransactionType.Expense, today, today.AddHours(2))
        };

        var result = HeaderQuickSearchEngine.Search(transactions, "shared").ToList();

        Assert.Equal([3, 2, 1], result.Select(item => item.Id));
        Assert.True(result[0].IsExpense);
        Assert.True(result[1].IsIncome);
    }

    private static TransactionVM Create(
        int id,
        string name,
        TransactionType type = TransactionType.Expense,
        DateTime? occurredOn = null,
        DateTime? loggedOn = null) => new()
    {
        Id = id,
        Name = name,
        Type = type,
        Amount = 10m,
        OccurredOn = occurredOn ?? DateTime.Today,
        LoggedOn = loggedOn ?? DateTime.Now
    };
}
