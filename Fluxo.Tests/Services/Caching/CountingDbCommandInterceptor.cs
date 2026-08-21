using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Fluxo.Tests.Services.Caching;

internal sealed class CountingDbCommandInterceptor : DbCommandInterceptor
{
    private int _commandCount;

    internal int CommandCount => Volatile.Read(ref _commandCount);

    internal void Reset()
    {
        Interlocked.Exchange(ref _commandCount, 0);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Interlocked.Increment(ref _commandCount);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _commandCount);
        return ValueTask.FromResult(result);
    }
}
