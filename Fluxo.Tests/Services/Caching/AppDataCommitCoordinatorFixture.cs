using Fluxo.Core.Entities;
using Fluxo.Core.Interfaces.Operations;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Data.Context;
using Fluxo.Services.Caching;
using Fluxo.Services.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Fluxo.Tests.Services.Caching;

internal sealed class AppDataCommitCoordinatorFixture : IAsyncDisposable
{
    private AppDataCommitCoordinatorFixture(
        SqliteConnection connection,
        ServiceProvider provider,
        AppDataCache cache,
        AppDataCommitCoordinator coordinator)
    {
        Connection = connection;
        Provider = provider;
        Cache = cache;
        Coordinator = coordinator;
    }

    internal SqliteConnection Connection { get; }
    internal ServiceProvider Provider { get; }
    internal AppDataCache Cache { get; }
    internal AppDataCommitCoordinator Coordinator { get; }

    internal static async Task<AppDataCommitCoordinatorFixture> CreateAsync(
        params IInterceptor[] interceptors)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var counter = new CountingDbCommandInterceptor();
        var provider = CacheTestServiceProviderFactory.Create(connection, counter, interceptors);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FluxoDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        var log = provider.GetRequiredService<ILogService>();
        var cache = new AppDataCache(
            new AppDataCacheHydrator(provider.GetRequiredService<IDataOperationRunner>(), log),
            log);
        await cache.InitializeAsync();
        var coordinator = new AppDataCommitCoordinator(
            provider.GetRequiredService<IDataOperationRunner>(),
            cache,
            log);
        return new AppDataCommitCoordinatorFixture(connection, provider, cache, coordinator);
    }

    internal AppDataService CreateAppDataService()
    {
        return new AppDataService(Cache, Coordinator);
    }

    internal async Task<IReadOnlyList<Account>> ReadDatabaseAccountsAsync()
    {
        await using var scope = Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FluxoDbContext>();
        return await db.Accounts.AsNoTracking().OrderBy(account => account.Id).ToListAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Provider.DisposeAsync();
        await Connection.DisposeAsync();
    }
}
