using System.Diagnostics;
using Fluxo.Core.Interfaces.Operations;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fluxo.Services.Caching;

public sealed class AppDataCacheHydrator(IDataOperationRunner runner, ILogService logService)
{
    internal Task<AppDataSnapshot> HydrateAsync(long version, CancellationToken cancellationToken)
    {
        return runner.RunAsync("hydrate application data cache", async (scope, ct) =>
        {
            var stopwatch = Stopwatch.StartNew();
            var db = scope.ServiceProvider.GetRequiredService<FluxoDbContext>();
            var transactions = await db.Transactions.IgnoreAutoIncludes().AsNoTracking().ToListAsync(ct)
                .ConfigureAwait(false);
            var tags = await db.Tags.IgnoreAutoIncludes().AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
            var savingGoals = await db.SavingGoals.IgnoreAutoIncludes().AsNoTracking().ToListAsync(ct)
                .ConfigureAwait(false);
            var accounts = await db.Accounts.IgnoreAutoIncludes().AsNoTracking().ToListAsync(ct)
                .ConfigureAwait(false);
            var recurring = await db.RecurringTransactions.IgnoreAutoIncludes().AsNoTracking().ToListAsync(ct)
                .ConfigureAwait(false);
            var settings = await db.UserSettings.IgnoreAutoIncludes().AsNoTracking().ToListAsync(ct)
                .ConfigureAwait(false);
            var allocation = await db.BudgetAllocation.IgnoreAutoIncludes().AsNoTracking().SingleOrDefaultAsync(ct)
                .ConfigureAwait(false);

            var snapshot = await Task.Run(() => AppDataSnapshot.Create(
                version,
                transactions,
                tags,
                savingGoals,
                accounts,
                recurring,
                settings,
                allocation), ct).ConfigureAwait(false);

            stopwatch.Stop();
            logService.LogInformation(
                $"Application data cache hydrated. Version={version}; " +
                $"Transactions={transactions.Count}; Tags={tags.Count}; SavingGoals={savingGoals.Count}; " +
                $"Accounts={accounts.Count}; RecurringTransactions={recurring.Count}; " +
                $"UserSettings={settings.Count}; BudgetAllocation={(allocation is null ? 0 : 1)}; " +
                $"ElapsedMs={stopwatch.ElapsedMilliseconds}.");
            return snapshot;
        }, cancellationToken);
    }
}
