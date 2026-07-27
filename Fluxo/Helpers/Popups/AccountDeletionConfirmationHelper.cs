using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;

namespace Fluxo.Helpers.Popups;

public static class AccountDeletionConfirmationHelper
{
    public static async Task<string> BuildDeleteConfirmationMessageAsync(
        IAppDataService appData,
        int sourceId,
        string? fallbackSourceName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(appData);

        var sources = await appData.GetAccountsAsync(cancellationToken);
        var source = sources.FirstOrDefault(item => item.Id == sourceId);
        var sourceName = string.IsNullOrWhiteSpace(source?.Name) ? fallbackSourceName ?? "this source" : source.Name;
        var isOnlyFunctioningSource = source is not null && IsOnlyFunctioningSource(sources, source.Id);

        return BuildDeleteConfirmationMessage(sourceName, isOnlyFunctioningSource);
    }

    public static string BuildDeleteConfirmationMessage(string sourceName, bool isOnlyFunctioningSource)
    {
        if (isOnlyFunctioningSource)
            return $"{sourceName} is the only available source. Deleting it will lock the application. Proceed to delete {sourceName} and all of its data?\n\n**THIS ACTION CANNOT BE UNDONE**";

        return $"Delete {sourceName} and all of its data?\n\n**THIS ACTION CANNOT BE UNDONE**";
    }

    public static bool IsOnlyFunctioningSource(IEnumerable<Account> sources, int sourceId)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var functioningSourceIds = sources
            .Where(source => IsFunctioning(source.AccountType, source.IsEnabled, source.Balance, source.AccountLimit))
            .Select(source => source.Id)
            .ToList();

        return functioningSourceIds.Count == 1 && functioningSourceIds[0] == sourceId;
    }

    public static bool IsFunctioning(AccountType sourceType, bool isEnabled, decimal balance, decimal accountLimit)
    {
        if (!isEnabled)
            return false;

        return sourceType == AccountType.Credit
            ? accountLimit > 0m
            : balance > 0m;
    }
}
