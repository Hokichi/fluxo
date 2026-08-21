using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.History;
using Fluxo.Core.Interfaces.Services;
using System.Globalization;

namespace Fluxo.Services.History;

public sealed class CompositeLogMemoryAction(string description, IReadOnlyList<ILogMemoryAction> actions)
    : ILogMemoryAction
{
    public string Description { get; } = description;
    public IReadOnlyList<ILogMemoryAction> Actions { get; } = actions;
    public string Title => $"{Description} Completed";
    public string Summary => $"{Description} completed";
    public string Details => string.Join(" · ", Actions.Select(action => action.Title));

    public async Task RevertAsync(IAppDataService appData, CancellationToken cancellationToken = default)
    {
        foreach (var action in actions.Reverse())
            await action.RevertAsync(appData, cancellationToken);
    }

    public async Task ReapplyAsync(IAppDataService appData, CancellationToken cancellationToken = default)
    {
        foreach (var action in actions)
            await action.ReapplyAsync(appData, cancellationToken);
    }
}
