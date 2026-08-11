using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.ViewModels.Entities;
using Fluxo.Views.Shell.Main;
using Fluxo.Helpers.MainWindow;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.Views.Shell.Main;

public sealed class TransactionDetailTargetResolverTests
{
    [Fact]
    public async Task TransactionDetailTargetResolver_ResolveAsync_ReturnsNull_WhenTransactionIdDoesNotExist()
    {
        var appData = Substitute.For<IAppDataService>();

        Assert.Null(await TransactionDetailTargetResolver.ResolveAsync(99, appData));
    }

    [Fact]
    public async Task TransactionDetailTargetResolver_ResolveAsync_LoadsTransactionById_WhenItHasNoParent()
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetTransactionByIdAsync(7, Arg.Any<CancellationToken>()).Returns(new Transaction
        {
            Id = 7,
            Type = TransactionType.Expense,
            Name = "Groceries",
            SourceAccountId = 1,
            Account = new Account { Id = 1, Name = "Checking" }
        });

        var result = await TransactionDetailTargetResolver.ResolveAsync(7, appData);

        Assert.NotNull(result);
        Assert.Equal(7, result.Id);
        Assert.Equal("Groceries", result.Name);
    }

    [Fact]
    public async Task TransactionDetailTargetResolver_ResolveAsync_LoadsParent_WhenTransactionIdBelongsToChild()
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetTransactionByIdAsync(4, Arg.Any<CancellationToken>()).Returns(new Transaction
        {
            Id = 4,
            Type = TransactionType.Expense,
            Name = "Child",
            ParentTransactionId = 3,
            SourceAccountId = 1,
            Account = new Account { Id = 1, Name = "Checking" }
        });
        appData.GetTransactionByIdAsync(3, Arg.Any<CancellationToken>()).Returns(new Transaction
        {
            Id = 3,
            Type = TransactionType.Expense,
            Name = "Parent",
            SourceAccountId = 1,
            Account = new Account { Id = 1, Name = "Checking" }
        });

        var result = await TransactionDetailTargetResolver.ResolveAsync(4, appData);

        Assert.NotNull(result);
        Assert.Equal(3, result.Id);
        Assert.Equal("Parent", result.Name);
    }

    [Fact]
    public async Task TransactionDetailTargetResolver_ResolveAsync_ReturnsTransaction_WhenItHasNoParent()
    {
        var appData = Substitute.For<IAppDataService>();
        var transaction = new TransactionVM { Id = 7 };

        Assert.Same(transaction, await TransactionDetailTargetResolver.ResolveAsync(transaction, appData));
        await appData.DidNotReceiveWithAnyArgs().GetTransactionByIdAsync(default, default);
    }

    [Theory]
    [InlineData(TransactionType.Expense)]
    [InlineData(TransactionType.Income)]
    public async Task TransactionDetailTargetResolver_ResolveAsync_ReturnsParentForEitherType(TransactionType type)
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetTransactionByIdAsync(3, Arg.Any<CancellationToken>()).Returns(new Transaction
        {
            Id = 3,
            Type = type,
            Name = "Parent",
            SourceAccountId = 1,
            Account = new Account { Id = 1, Name = "Checking" }
        });

        var result = await TransactionDetailTargetResolver.ResolveAsync(
            new TransactionVM { Id = 4, ParentTransactionId = 3 }, appData);

        Assert.Equal(3, result.Id);
        Assert.Equal(type, result.Type);
        Assert.Equal("Parent", result.Name);
    }
}
