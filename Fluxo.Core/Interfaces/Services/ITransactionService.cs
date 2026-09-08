namespace Fluxo.Core.Interfaces.Services;

public interface ITransactionService
{
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
    Task PostTerminationCleanupAsync(CancellationToken cancellationToken = default);
}
