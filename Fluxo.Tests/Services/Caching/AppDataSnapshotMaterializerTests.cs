using Fluxo.Services.Caching;
using Xunit;

namespace Fluxo.Tests.Services.Caching;

public sealed class AppDataSnapshotMaterializerTests
{
    [Fact]
    public void TransactionById_ReturnsDetachedGraphThatCannotMutateSnapshot()
    {
        var snapshot = AppDataSnapshotFixture.Create();
        var materializer = new AppDataSnapshotMaterializer(snapshot);
        var first = materializer.TransactionById(10)!;
        first.Name = "Mutated transaction";
        first.Account.Name = "Mutated account";
        first.Tag!.Name = "Mutated tag";

        var second = materializer.TransactionById(10)!;

        Assert.Equal("Transaction 10", second.Name);
        Assert.Equal("Main", second.Account.Name);
        Assert.Equal("Food", second.Tag!.Name);
        Assert.NotSame(first, second);
        Assert.NotSame(first.Account, second.Account);
        Assert.NotSame(first.Tag, second.Tag);
    }
}
