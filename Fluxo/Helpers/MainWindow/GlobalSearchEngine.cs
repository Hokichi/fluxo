using Fluxo.DataModels.Popups.GlobalSearch;

namespace Fluxo.Helpers.MainWindow;

public static class GlobalSearchEngine
{
    private const int MinimumQueryLength = 4;
    private const int ResultsPerType = 5;

    public static IReadOnlyList<GlobalSearchResultGroup> Search(
        IReadOnlyList<GlobalSearchResult> candidates,
        string? query)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var normalizedQuery = query?.Trim();
        if (string.IsNullOrEmpty(normalizedQuery) || normalizedQuery.Length < MinimumQueryLength)
            return [];

        var matching = candidates.Where(candidate =>
            candidate.Name.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase));

        return Enum.GetValues<GlobalSearchResultType>()
            .Select(type => new GlobalSearchResultGroup(
                type,
                OrderResults(matching.Where(candidate => candidate.Type == type), type)
                    .Take(ResultsPerType)
                    .ToList()))
            .Where(group => group.Results.Count > 0)
            .ToList();
    }

    public static GlobalSearchResultType? ResolveSelectedType(
        IReadOnlyList<GlobalSearchResultGroup> groups,
        GlobalSearchResultType? currentType)
    {
        ArgumentNullException.ThrowIfNull(groups);

        if (currentType is not null && groups.Any(group => group.Type == currentType))
            return currentType;

        return groups.FirstOrDefault()?.Type;
    }

    private static IOrderedEnumerable<GlobalSearchResult> OrderResults(
        IEnumerable<GlobalSearchResult> results,
        GlobalSearchResultType type) => type == GlobalSearchResultType.Transactions
        ? results.OrderByDescending(result => result.LoggedOn)
            .ThenBy(result => result.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.EntityId)
        : results.OrderBy(result => result.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.EntityId);
}
