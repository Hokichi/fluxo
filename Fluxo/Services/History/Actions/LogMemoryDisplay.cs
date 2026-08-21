using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.History;
using Fluxo.Core.Interfaces.Services;
using System.Globalization;

namespace Fluxo.Services.History;

internal static class LogMemoryDisplay
{
    internal static string Amount(decimal value) =>
        value.ToString("#,0.##", CultureInfo.CurrentCulture);

    internal static string OptionalAmount(decimal? value) =>
        value.HasValue ? Amount(value.Value) : "None";

    internal static string Date(DateTime value) =>
        value.ToString("MMM d, yyyy", CultureInfo.CurrentCulture);

    internal static string Text(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "None" : value;

    internal static string YesNo(bool value) => value ? "Yes" : "No";

    internal static string TransactionNoun(TransactionType type) =>
        type == TransactionType.Expense ? "Expense" : "Income";

    internal static string Changes(params (string Label, string Before, string After)[] values) =>
        string.Join(" · ", values
            .Where(value => !string.Equals(value.Before, value.After, StringComparison.Ordinal))
            .Select(value => $"{value.Label}: {value.Before} → {value.After}"));
}
