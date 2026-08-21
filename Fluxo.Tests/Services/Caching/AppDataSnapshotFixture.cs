using Fluxo.Core.Entities;
using Fluxo.Services.Caching;

namespace Fluxo.Tests.Services.Caching;

internal static class AppDataSnapshotFixture
{
    internal static AppDataSnapshot Create()
    {
        var account = new Account { Id = 1, Name = "Main", IsEnabled = true };
        var tag = new Tag { Id = 2, Name = "Food", HexCode = "#ffffff" };
        var transaction = CacheEntityFactory.CreateTransaction(10, account, tag);

        return AppDataSnapshot.Create(
            1,
            [transaction],
            [tag],
            [],
            [account],
            [],
            [],
            null);
    }
}
