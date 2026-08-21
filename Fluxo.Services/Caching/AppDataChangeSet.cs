using Fluxo.Core.Entities;
using Fluxo.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Fluxo.Services.Caching;

internal sealed class AppDataChangeSet(IReadOnlyList<AppDataChange> changes)
{
    internal IReadOnlyList<AppDataChange> Changes { get; } = changes;
    internal bool IsEmpty => Changes.Count == 0;

    internal static AppDataChangeSet Capture(FluxoDbContext dbContext)
    {
        var changes = dbContext.ChangeTracker.Entries()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(entry => IsCachedEntity(entry.Entity))
            .Select(entry => new AppDataChange(ToChangeKind(entry.State), entry.Entity))
            .ToArray();
        return new AppDataChangeSet(changes);
    }

    private static bool IsCachedEntity(object entity)
    {
        return entity is Transaction or Tag or SavingGoal or Account or RecurringTransaction or UserSettings or
            BudgetAllocation;
    }

    private static AppDataChangeKind ToChangeKind(EntityState state)
    {
        return state switch
        {
            EntityState.Added => AppDataChangeKind.Added,
            EntityState.Modified => AppDataChangeKind.Modified,
            EntityState.Deleted => AppDataChangeKind.Deleted,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
        };
    }
}
