using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Fluxo.Tests.Services.Caching;

internal sealed class FailingSaveChangesInterceptor : SaveChangesInterceptor
{
    internal bool IsEnabled { get; set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (IsEnabled)
            throw new InvalidOperationException("Forced save failure.");

        return ValueTask.FromResult(result);
    }
}
