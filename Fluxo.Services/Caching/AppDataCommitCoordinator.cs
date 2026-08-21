using System.Diagnostics;
using Fluxo.Core.Interfaces;
using Fluxo.Core.Interfaces.Operations;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Data.Context;
using Microsoft.Extensions.DependencyInjection;

namespace Fluxo.Services.Caching;

public sealed class AppDataCommitCoordinator(
    IDataOperationRunner runner,
    AppDataCache cache,
    ILogService logService)
{
    private readonly SemaphoreSlim _writerGate = new(1, 1);

    public async Task SaveAsync(
        IReadOnlyList<Func<IUnitOfWork, CancellationToken, Task>> operations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operations);
        if (operations.Count == 0)
            return;

        await _writerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var candidate = await runner.RunInTransactionAsync<AppDataSnapshot?>(
                "save data changes",
                async (scope, ct) =>
                {
                    foreach (var operation in operations)
                        await operation(scope.UnitOfWork, ct).ConfigureAwait(false);

                    var dbContext = scope.ServiceProvider.GetRequiredService<FluxoDbContext>();
                    var changes = AppDataChangeSet.Capture(dbContext);
                    if (changes.IsEmpty)
                        return null;

                    await scope.UnitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
                    return cache.CreateCandidate(changes);
                },
                cancellationToken).ConfigureAwait(false);

            if (candidate is null)
                return;

            cache.Publish(candidate);
            stopwatch.Stop();
            try
            {
                logService.LogInformation(
                    $"Application data commit published. Version={candidate.Version}; " +
                    $"ElapsedMs={stopwatch.ElapsedMilliseconds}.");
            }
            catch
            {
                // Durable commit and cache publication must not fail because diagnostics failed.
            }
        }
        finally
        {
            _writerGate.Release();
        }
    }

    public async Task RebuildAsync(CancellationToken cancellationToken = default)
    {
        await _writerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var candidate = await cache.CreateRebuildCandidateAsync(cancellationToken).ConfigureAwait(false);
            cache.Publish(candidate);
        }
        finally
        {
            _writerGate.Release();
        }
    }
}
