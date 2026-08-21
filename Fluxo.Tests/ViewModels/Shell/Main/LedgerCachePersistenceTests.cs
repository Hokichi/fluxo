using AutoMapper;
using Fluxo.Core.DTO;
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
        var transactionService = Substitute.For<ITransactionService>();
        transactionService.GetAllAsync(Arg.Any<CancellationToken>()).Returns([
            new TransactionDto
            {
                Id = 8,
                Type = TransactionType.Expense,
                Name = "Expense",
                OccurredOn = DateTime.Today,
                LoggedOn = DateTime.Today
            }
        ]);
        var accountService = Substitute.For<IAccountService>();
        accountService.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        var tagService = Substitute.For<ITagService>();
        tagService.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        var mapper = new MapperConfiguration(
            configuration => configuration.AddProfile<DtoViewModelProfile>(),
            NullLoggerFactory.Instance).CreateMapper();
        var viewModel = new LedgerVM(
            transactionService,
            accountService,
            tagService,
            appData,
            mapper);
        await viewModel.LoadAsync();

        await viewModel.RemoveTransactionCommand.ExecuteAsync(
            Assert.Single(viewModel.GetVisibleTransactionsForExport()));

        Assert.True(entity.IsForDeletion);
        appData.Received(1).UpdateTransaction(entity);
        await appData.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
