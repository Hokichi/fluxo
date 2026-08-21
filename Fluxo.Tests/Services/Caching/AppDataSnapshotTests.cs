using Fluxo.Core.Entities;
using Fluxo.Services.Caching;
using Xunit;

namespace Fluxo.Tests.Services.Caching;

public sealed class AppDataSnapshotTests
{
    [Fact]
    public void Create_PreservesHiddenRowsAndBuildsTransactionIndexes()
    {
        var account = new Account { Id = 1, Name = "Main", IsEnabled = true };
        var tag = new Tag { Id = 2, Name = "Food", HexCode = "#ffffff" };
        var active = CacheEntityFactory.CreateTransaction(
            10,
            account,
            tag,
            occurredOn: new DateTime(2026, 8, 20));
        var deleted = CacheEntityFactory.CreateTransaction(
            11,
            account,
            tag,
            isForDeletion: true,
            occurredOn: new DateTime(2026, 8, 21));

        var snapshot = AppDataSnapshot.Create(
            1,
            [active, deleted],
            [tag],
            [],
            [account],
            [],
            [],
            null);

        Assert.Equal(2, snapshot.Transactions.Count);
        Assert.Equal(1, snapshot.TagUsageCounts[tag.Id]);
        Assert.Equal([11, 10], snapshot.TransactionOrder.Select(key => key.Id));
    }

    [Fact]
    public void Create_WhenRequiredAccountIsMissing_RejectsSnapshot()
    {
        var transaction = new Transaction
        {
            Id = 1,
            SourceAccountId = 404,
            Name = "Broken",
            Notes = string.Empty
        };

        Assert.Throws<InvalidDataException>(() => AppDataSnapshot.Create(
            1,
            [transaction],
            [],
            [],
            [],
            [],
            [],
            null));
    }
}
