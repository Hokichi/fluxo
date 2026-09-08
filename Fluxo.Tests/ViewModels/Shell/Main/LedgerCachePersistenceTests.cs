using AutoMapper;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Mappings;
using Fluxo.ViewModels.Shell.Main;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.ViewModels.Shell.Main;

public sealed class LedgerCachePersistenceTests
{
    [Fact]
    public async Task RemoveTransaction_SoftDeletesCachedEntityAndSavesOnce()
    {
        var entity = new Transaction { Id = 8, Type = TransactionType.Expense, Name = "Expense" };
        var appData = Substitute.For<IAppDataService>();
        appData.GetTransactionByIdAsync(8, Arg.Any<CancellationToken>()).Returns(entity);
        appData.GetTransactionsAsync(Arg.Any<CancellationToken>()).Returns([
            new Transaction
            {
                Id = 8,
                Type = TransactionType.Expense,
                Name = "Expense",
                OccurredOn = DateTime.Today,
                LoggedOn = DateTime.Today,
                Account = new Account { Name = "Checking" }
            }
        ]);
        appData.GetAccountsAsync(Arg.Any<CancellationToken>()).Returns([]);
        appData.GetTagsAsync(Arg.Any<CancellationToken>()).Returns([]);
        var mapper = new MapperConfiguration(
            configuration => configuration.AddProfile<EntityViewModelProfile>(),
            NullLoggerFactory.Instance).CreateMapper();
        var viewModel = new LedgerVM(appData, mapper);
        await viewModel.LoadAsync();

        await viewModel.RemoveTransactionCommand.ExecuteAsync(
            Assert.Single(viewModel.GetVisibleTransactionsForExport()));

        Assert.True(entity.IsForDeletion);
        appData.Received(1).UpdateTransaction(entity);
        await appData.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
