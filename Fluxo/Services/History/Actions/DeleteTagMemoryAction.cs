using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.History;
using Fluxo.Core.Interfaces.Services;
using System.Globalization;

namespace Fluxo.Services.History;

public sealed class DeleteTagMemoryAction(TagMemorySnapshot snapshot) : ILogMemoryAction
{
    public string Description => "Delete tag";
    public string Title => $"{snapshot.Name} Deleted";
    public string Summary => "Tag deleted";
    public string Details => snapshot.SpendingLimit.HasValue
        ? $"Limit {LogMemoryDisplay.Amount(snapshot.SpendingLimit.Value)}"
        : "No spending limit";

    public async Task RevertAsync(IAppDataService appData, CancellationToken cancellationToken = default)
    {
        if (await appData.GetTagByIdAsync(snapshot.TagId, cancellationToken) is not null)
            return;

        var tag = new Tag
        {
            Id = snapshot.TagId,
            Name = snapshot.Name,
            HexCode = snapshot.HexCode,
            SpendingLimit = snapshot.SpendingLimit
        };

        await appData.AddTagAsync(tag, cancellationToken);
    }

    public async Task ReapplyAsync(IAppDataService appData, CancellationToken cancellationToken = default)
    {
        var tag = await appData.GetTagByIdAsync(snapshot.TagId, cancellationToken);
        if (tag is null)
            return;

        appData.RemoveTag(tag);
    }
}
