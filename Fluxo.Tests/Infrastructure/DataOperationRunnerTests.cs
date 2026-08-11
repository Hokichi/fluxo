using Fluxo.Core.Interfaces;
using Fluxo.Core.Interfaces.Operations;
using Fluxo.Core.Interfaces.Repositories;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Core.Entities;
using Fluxo.Core.Exceptions;
using Fluxo.Data.Context;
using Fluxo.Data.Extensions;
using Fluxo.Data.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.Infrastructure;

public sealed class DataOperationRunnerTests
{
    [Fact]
    public async Task DataOperationRunner_RunInTransactionAsync_ReturnsOperationResult()
    {
        using var serviceProvider = CreateServiceProvider();
        var runner = serviceProvider.GetRequiredService<IDataOperationRunner>();

        var result = await runner.RunInTransactionAsync("return result", (_, _) => Task.FromResult("done"));

        Assert.Equal("done", result);
    }

    [Fact]
    public async Task DataOperationRunner_RunInTransactionAsync_WhenLaterSaveFails_RollsBackEarlierSave()
    {
        var options = new DbContextOptionsBuilder<FluxoDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        await using var dbContext = new FluxoDbContext(options);
        await dbContext.Database.OpenConnectionAsync();
        await dbContext.Database.EnsureCreatedAsync();
        using var services = new ServiceCollection().AddSingleton(dbContext).BuildServiceProvider();
        var scope = Substitute.For<IDataOperationScope>();
        scope.ServiceProvider.Returns(services);
        var factory = Substitute.For<IDataOperationScopeFactory>();
        factory.CreateAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(scope));
        var log = Substitute.For<ILogService>();
        log.CreateFailureMessage(Arg.Any<string>()).Returns("failed");
        var runner = new DataOperationRunner(factory, log);

        await Assert.ThrowsAsync<DataOperationException>(() =>
            runner.RunInTransactionAsync("revert history range", async (_, ct) =>
            {
                dbContext.UserSettings.Add(new UserSettings { Name = "first", Value = "1" });
                await dbContext.SaveChangesAsync(ct);
                dbContext.UserSettings.Add(new UserSettings { Name = "second", Value = "2" });
                await dbContext.SaveChangesAsync(ct);
                throw new InvalidOperationException("stop batch");
            }));

        dbContext.ChangeTracker.Clear();
        Assert.Empty(await dbContext.UserSettings.ToListAsync());
    }

    [Fact]
    public async Task DataOperationRunner_RunAsync_UsesSharedScopedInstancesWithinSingleOperation()
    {
        using var serviceProvider = CreateServiceProvider();

        var runner = serviceProvider.GetRequiredService<IDataOperationRunner>();

        await runner.RunAsync((scope, _) =>
        {
            var unitOfWorkFromScope = scope.UnitOfWork;
            var unitOfWorkFromResolver = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var transactionsFromScope = scope.ServiceProvider.GetRequiredService<ITransactionRepository>();
            var transactionsFromUnitOfWork = unitOfWorkFromScope.Transactions;

            Assert.Same(unitOfWorkFromScope, unitOfWorkFromResolver);
            Assert.Same(transactionsFromScope, transactionsFromUnitOfWork);

            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task DataOperationRunner_RunAsync_CreatesDifferentUnitOfWorkForDifferentOperations()
    {
        using var serviceProvider = CreateServiceProvider();

        var runner = serviceProvider.GetRequiredService<IDataOperationRunner>();
        IUnitOfWork? first = null;
        IUnitOfWork? second = null;

        await runner.RunAsync((scope, _) =>
        {
            first = scope.UnitOfWork;
            return Task.CompletedTask;
        });

        await runner.RunAsync((scope, _) =>
        {
            second = scope.UnitOfWork;
            return Task.CompletedTask;
        });

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.NotSame(first!.Transactions, second!.Transactions);
    }

    private static ServiceProvider CreateServiceProvider()
    {
        return new ServiceCollection()
            .AddSingleton(Substitute.For<ILogService>())
            .AddFluxoData()
            .BuildServiceProvider();
    }
}
