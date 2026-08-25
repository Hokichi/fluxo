using Fluxo.Core.Interfaces.Services;

namespace Fluxo.Services.Persistence;

public sealed class AppDataPersistenceBatch : IDisposable
{
    private readonly IAppDataService _appData;
    private readonly AppDataService? _scopedAppData;
    private readonly Guid _scopeId;
    private readonly Guid? _previousScopeId;
    private bool _finished;
    private bool _disposed;

    private AppDataPersistenceBatch(IAppDataService appData)
    {
        _appData = appData;
        if (appData is not AppDataService scopedAppData)
            return;

        _scopedAppData = scopedAppData;
        (_scopeId, _previousScopeId) = scopedAppData.EnterPersistenceBatch();
    }

    public static AppDataPersistenceBatch Begin(IAppDataService appData)
    {
        ArgumentNullException.ThrowIfNull(appData);
        return new AppDataPersistenceBatch(appData);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (_scopedAppData is null)
            await _appData.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        else
            await _scopedAppData.SaveChangesAsync(_scopeId, cancellationToken).ConfigureAwait(false);
        _finished = true;
    }

    public void Discard()
    {
        if (_finished)
            return;

        if (_scopedAppData is null)
            _appData.DiscardPendingChanges();
        else
            _scopedAppData.DiscardPendingChanges(_scopeId);
        _finished = true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (!_finished)
            Discard();
        _scopedAppData?.ExitPersistenceBatch(_scopeId, _previousScopeId);
        _disposed = true;
    }
}
