using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.History;
using Fluxo.Core.Interfaces.Services;
using System.Globalization;

namespace Fluxo.Services.History;

public sealed class EditTagMemoryAction(
    TagMemorySnapshot before,
    TagMemorySnapshot after) : ILogMemoryAction
{
    public string Description => "Edit tag";
    public string Title => $"{after.Name} Updated";
    public string Summary => "Tag information updated";
    public string Details => LogMemoryDisplay.Changes(
        ("Name", before.Name, after.Name),
        ("Color", before.HexCode, after.HexCode),
        ("Spending limit", LogMemoryDisplay.OptionalAmount(before.SpendingLimit), LogMemoryDisplay.OptionalAmount(after.SpendingLimit)));

    public Task RevertAsync(IAppDataService appData, CancellationToken cancellationToken = default)
    {
        return ApplySnapshotAsync(appData, before, cancellationToken);
    }

    public Task ReapplyAsync(IAppDataService appData, CancellationToken cancellationToken = default)
    {
        return ApplySnapshotAsync(appData, after, cancellationToken);
    }

    private static async Task ApplySnapshotAsync(IAppDataService appData, TagMemorySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var tag = await appData.GetTagByIdAsync(snapshot.TagId, cancellationToken);
        if (tag is null)
            return;

        tag.Name = snapshot.Name;
        tag.HexCode = snapshot.HexCode;
        tag.SpendingLimit = snapshot.SpendingLimit;
        appData.UpdateTag(tag);
    }
}
