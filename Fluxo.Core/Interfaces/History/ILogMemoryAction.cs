using Fluxo.Core.Interfaces.Services;

namespace Fluxo.Core.Interfaces.History;

public interface ILogMemoryAction
{
    string Description { get; }

    string Title => Description;

    string Summary => string.Empty;

    string Details => string.Empty;

    Task RevertAsync(IAppDataService appData, CancellationToken cancellationToken = default);

    Task ReapplyAsync(IAppDataService appData, CancellationToken cancellationToken = default);
}
