using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Fluxo.Tests.Services.Caching;

internal sealed class BlockingSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal bool IsEnabled { get; set; }
    internal Task Entered => _entered.Task;

    internal void Release()
    {
        _release.TrySetResult();
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (IsEnabled)
        {
            _entered.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
        }

        return result;
    }
}
