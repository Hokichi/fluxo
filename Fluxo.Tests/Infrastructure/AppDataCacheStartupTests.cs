using Fluxo.Core.Entities;
using Fluxo.Core.Interfaces.Caching;
using Fluxo.Core.Interfaces.Operations;
using Fluxo.Services.Persistence;
using Fluxo.Tests.Services.Caching;
using Fluxo.ViewModels.Shell.QuickSetupWizard;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.Infrastructure;

public sealed class AppDataCacheStartupTests
{
    [Fact]
    public async Task InitializeApplicationDataAsync_WarmsAfterMaintenanceAndBeforeMainInitialization()
    {
        var events = new List<string>();
        var cache = Substitute.For<IAppDataCache>();
        cache.InitializeAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            events.Add("cache");
            return Task.CompletedTask;
        });

        await App.InitializeApplicationDataAsync(
            false,
            cache,
            () => RecordAsync(events, "cleanup"),
            () => RecordAsync(events, "registration"),
            () => RecordAsync(events, "main"),
            () => RecordAsync(events, "settle"));

        Assert.Equal(
            ["cleanup", "settle", "registration", "settle", "cache", "settle", "main"],
            events);
    }

    [Fact]
    public async Task InitializeApplicationDataAsync_WarmFailurePreventsMainInitialization()
    {
        var mainInitialized = false;
        var cache = Substitute.For<IAppDataCache>();
        cache.InitializeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("warm failed")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => App.InitializeApplicationDataAsync(
            false,
            cache,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () =>
            {
                mainInitialized = true;
                return Task.CompletedTask;
            },
            () => Task.CompletedTask));

        Assert.False(mainInitialized);
    }

    [Fact]
    public async Task WizardStaging_RollbackLeavesCacheUntouchedAndCommitRebuildsOnce()
    {
        await using var fixture = await AppDataCommitCoordinatorFixture.CreateAsync();
        var initialVersion = fixture.Cache.Version;

        await using (var scope = await fixture.Services.GetRequiredService<IDataOperationScopeFactory>().CreateAsync())
        {
            var transaction = await scope.ServiceProvider.GetRequiredService<Fluxo.Data.Context.FluxoDbContext>()
                .Database.BeginTransactionAsync();
            var staged = new AppDataService(scope.UnitOfWork);
            await staged.AddAccountAsync(new Account { Name = "Rolled back", IsEnabled = true });
            await staged.SaveChangesAsync();

            Assert.Equal(initialVersion, fixture.Cache.Version);
            Assert.Empty(await fixture.Cache.GetAccountsAsync());
            await transaction.RollbackAsync();
        }

        Assert.Equal(initialVersion, fixture.Cache.Version);
        Assert.Empty(await fixture.Cache.GetAccountsAsync());

        await using (var scope = await fixture.Services.GetRequiredService<IDataOperationScopeFactory>().CreateAsync())
        {
            var transaction = await scope.ServiceProvider.GetRequiredService<Fluxo.Data.Context.FluxoDbContext>()
                .Database.BeginTransactionAsync();
            var staged = new AppDataService(scope.UnitOfWork);
            await staged.AddAccountAsync(new Account { Name = "Committed", IsEnabled = true });
            await staged.SaveChangesAsync();

            await QuickSetupWizardVM.CommitStagedDataAsync(
                () => transaction.CommitAsync(),
                () => fixture.Coordinator.RebuildAsync());
        }

        Assert.Equal(initialVersion + 1, fixture.Cache.Version);
        Assert.Equal("Committed", Assert.Single(await fixture.Cache.GetAccountsAsync()).Name);
    }

    private static Task RecordAsync(ICollection<string> events, string value)
    {
        events.Add(value);
        return Task.CompletedTask;
    }
}
