using Fluxo.Core.Entities;
using Fluxo.Core.Interfaces.Services;
using Fluxo.DataModels.Popups.GlobalSearch;
using Fluxo.Helpers.MainWindow;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.Views.Shell.Main;

public sealed class GlobalSearchCandidateLoaderTests
{
    [Fact]
    public async Task LoadAsync_UsesCachedDataAndExcludesEntitiesMarkedForDeletion()
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetTransactionsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new Transaction { Id = 1, Name = "Visible transaction", LoggedOn = DateTime.UnixEpoch },
            new Transaction { Id = 2, Name = "Deleted transaction", IsForDeletion = true }
        ]);
        appData.GetAccountsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new Account { Id = 3, Name = "Visible account" },
            new Account { Id = 4, Name = "Deleted account", IsForDeletion = true }
        ]);
        appData.GetTagsAsync(Arg.Any<CancellationToken>()).Returns([new Tag { Id = 5, Name = "Visible tag", HexCode = "#000000" }]);
        appData.GetSavingGoalsAsync(Arg.Any<CancellationToken>()).Returns([new SavingGoal { Id = 6, Name = "Visible goal" }]);

        var candidates = await new GlobalSearchCandidateLoader(appData).LoadAsync();

        Assert.Contains(candidates, result => result is { Type: GlobalSearchResultType.Transactions, EntityId: 1, Name: "Visible transaction" });
        Assert.Contains(candidates, result => result is { Type: GlobalSearchResultType.Accounts, EntityId: 3, Name: "Visible account" });
        Assert.Contains(candidates, result => result is { Type: GlobalSearchResultType.Tags, EntityId: 5, Name: "Visible tag" });
        Assert.Contains(candidates, result => result is { Type: GlobalSearchResultType.Goals, EntityId: 6, Name: "Visible goal" });
        Assert.DoesNotContain(candidates, result => result.EntityId is 2 or 4);
        Assert.Contains(candidates, result => result is { Type: GlobalSearchResultType.Features, Name: "New transaction" });
        Assert.Contains(candidates, result => result is { Type: GlobalSearchResultType.Settings, Name: "Check for updates" });
        await appData.Received(1).GetTransactionsAsync(Arg.Any<CancellationToken>());
        await appData.Received(1).GetAccountsAsync(Arg.Any<CancellationToken>());
        await appData.Received(1).GetTagsAsync(Arg.Any<CancellationToken>());
        await appData.Received(1).GetSavingGoalsAsync(Arg.Any<CancellationToken>());
    }
}
