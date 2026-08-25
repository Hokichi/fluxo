namespace Fluxo.DataModels.Popups.GlobalSearch;

public sealed record GlobalSearchResultGroup(
    GlobalSearchResultType Type,
    IReadOnlyList<GlobalSearchResult> Results)
{
    public string Name => Type.ToString();
}
