using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.History;
using Fluxo.Core.Interfaces.Services;
using System.Globalization;

namespace Fluxo.Services.History;

public sealed record TagMemorySnapshot(
    int TagId,
    string Name,
    string HexCode,
    decimal? SpendingLimit)
{
    public static TagMemorySnapshot Create(Tag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        return new TagMemorySnapshot(
            tag.Id,
            tag.Name,
            tag.HexCode,
            tag.SpendingLimit);
    }
}
