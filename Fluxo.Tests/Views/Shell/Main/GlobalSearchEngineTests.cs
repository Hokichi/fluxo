using Fluxo.DataModels.Popups.GlobalSearch;
using Fluxo.Helpers.MainWindow;
using Xunit;

namespace Fluxo.Tests.Views.Shell.Main;

public sealed class GlobalSearchEngineTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    public void Search_ReturnsNoGroupsUntilTrimmedQueryHasFourCharacters(string? query)
    {
        var results = GlobalSearchEngine.Search([Result(GlobalSearchResultType.Accounts, "Checking")], query);

        Assert.Empty(results);
    }

    [Fact]
    public void Search_GroupsByPriorityAndLimitsEachGroupToFiveNameMatches()
    {
        var candidates = new List<GlobalSearchResult>
        {
            Result(GlobalSearchResultType.Settings, "Search settings"),
            Result(GlobalSearchResultType.Features, "Search feature"),
            Result(GlobalSearchResultType.Goals, "Search goal"),
            Result(GlobalSearchResultType.Tags, "Search tag"),
            Result(GlobalSearchResultType.Accounts, "Search account")
        };
        candidates.AddRange(Enumerable.Range(1, 6).Select(index =>
            Result(GlobalSearchResultType.Transactions, $"Search transaction {index}", entityId: index, loggedOn: DateTime.UnixEpoch.AddDays(index))));

        var groups = GlobalSearchEngine.Search(candidates, "search");

        Assert.Equal(
            [
                GlobalSearchResultType.Transactions,
                GlobalSearchResultType.Accounts,
                GlobalSearchResultType.Tags,
                GlobalSearchResultType.Goals,
                GlobalSearchResultType.Features,
                GlobalSearchResultType.Settings
            ],
            groups.Select(group => group.Type));
        Assert.Equal(5, groups[0].Results.Count);
        Assert.Equal(6, groups.Count);
    }

    [Fact]
    public void Search_OrdersTransactionsByNewestLoggedOnAndOtherTypesAlphabetically()
    {
        var candidates = new[]
        {
            Result(GlobalSearchResultType.Transactions, "Search older", entityId: 1, loggedOn: DateTime.UnixEpoch.AddDays(1)),
            Result(GlobalSearchResultType.Transactions, "Search newer", entityId: 2, loggedOn: DateTime.UnixEpoch.AddDays(3)),
            Result(GlobalSearchResultType.Transactions, "Search middle", entityId: 3, loggedOn: DateTime.UnixEpoch.AddDays(2)),
            Result(GlobalSearchResultType.Accounts, "Search Zebra", entityId: 1),
            Result(GlobalSearchResultType.Accounts, "Search Apple", entityId: 2)
        };

        var groups = GlobalSearchEngine.Search(candidates, "search");

        Assert.Equal([2, 3, 1], groups[0].Results.Select(result => result.EntityId));
        Assert.Equal(["Search Apple", "Search Zebra"], groups[1].Results.Select(result => result.Name));
    }

    [Fact]
    public void ResolveSelectedType_UsesCurrentWhenStillAvailableOtherwiseFirstAvailable()
    {
        var groups = GlobalSearchEngine.Search(
        [
            Result(GlobalSearchResultType.Accounts, "Search account"),
            Result(GlobalSearchResultType.Settings, "Search setting")
        ],
        "search");

        Assert.Equal(GlobalSearchResultType.Settings, GlobalSearchEngine.ResolveSelectedType(groups, GlobalSearchResultType.Settings));
        Assert.Equal(GlobalSearchResultType.Accounts, GlobalSearchEngine.ResolveSelectedType(groups, GlobalSearchResultType.Features));
        Assert.Null(GlobalSearchEngine.ResolveSelectedType([], GlobalSearchResultType.Accounts));
    }

    private static GlobalSearchResult Result(
        GlobalSearchResultType type,
        string name,
        int? entityId = null,
        DateTime? loggedOn = null) => new(type, name, EntityId: entityId, LoggedOn: loggedOn);
}
