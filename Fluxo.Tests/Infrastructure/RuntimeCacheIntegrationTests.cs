using Fluxo.Core.Entities;
using Fluxo.Core.Interfaces.Caching;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Data.Context;
using Fluxo.Data.Extensions;
using Fluxo.Extensions;
using Fluxo.Services.Notifications;
using Fluxo.Tests.Services.Caching;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.Infrastructure;

public sealed class RuntimeCacheIntegrationTests
{
    [Fact]
    public async Task WarmRuntime_ReadsIssueNoSqlAndSavePublishesDatabaseAndCacheTogether()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var interceptor = new CountingDbCommandInterceptor();
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ILogService>());
        services.AddFluxoData();
        services.AddFluxoPresentation();
        services.AddDbContext<FluxoDbContext>(options =>
            options.UseSqlite(connection).AddInterceptors(interceptor));
        await using var provider = services.BuildServiceProvider();

        await using (var seedScope = provider.CreateAsyncScope())
        {
            var database = seedScope.ServiceProvider.GetRequiredService<FluxoDbContext>();
            await database.Database.EnsureCreatedAsync();
            database.Accounts.Add(new Account { Name = "Checking", IsEnabled = true });
            database.Tags.Add(new Tag { Name = "Food", HexCode = "#00AA00" });
            database.SavingGoals.Add(new SavingGoal { Name = "Emergency", CreatedOn = DateTime.Today });
            database.UserSettings.Add(new UserSettings { Name = "theme", Value = "dark" });
            database.BudgetAllocation.Add(new BudgetAllocation());
            await database.SaveChangesAsync();
        }

        var cache = provider.GetRequiredService<IAppDataCache>();
        await cache.InitializeAsync();
        interceptor.Reset();

        await using (var runtimeScope = provider.CreateAsyncScope())
        {
            var appData = runtimeScope.ServiceProvider.GetRequiredService<IAppDataService>();
            await appData.GetTransactionsAsync();
            await appData.GetAccountsAsync();
            await appData.GetTagsAsync();
            await appData.GetSavingGoalsAsync();
            await appData.GetRecurringTransactionsAsync();
            await appData.GetUserSettingsAsync();
            await appData.GetBudgetAllocationAsync();
            await runtimeScope.ServiceProvider.GetRequiredService<IAnalyticsService>()
                .GetAnalyticsAsync(DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today));
            await runtimeScope.ServiceProvider.GetRequiredService<ICalendarService>()
                .GetCalendarDayAsync(DateOnly.FromDateTime(DateTime.Today));
            await runtimeScope.ServiceProvider.GetRequiredService<StartupNotificationEvaluator>().EvaluateAsync();

            Assert.Equal(0, interceptor.CommandCount);

            var version = cache.Version;
            await appData.AddAccountAsync(new Account { Name = "Savings", IsEnabled = true });
            await appData.SaveChangesAsync();

            Assert.Equal(version + 1, cache.Version);
            Assert.Contains(await appData.GetAccountsAsync(), account => account.Name == "Savings");
        }

        await using var verificationScope = provider.CreateAsyncScope();
        var verificationDatabase = verificationScope.ServiceProvider.GetRequiredService<FluxoDbContext>();
        Assert.True(await verificationDatabase.Accounts.AsNoTracking()
            .AnyAsync(account => account.Name == "Savings"));
    }
}
