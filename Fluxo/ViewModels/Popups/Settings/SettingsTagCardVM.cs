using System.Globalization;
using Fluxo.Core.Entities;
using Fluxo.Data.Enums;

namespace Fluxo.ViewModels.Popups.Settings;

public sealed class SettingsTagCardVM
{
    public int Id { get; private init; }
    public string Name { get; private init; } = string.Empty;
    public string HexCode { get; private init; } = string.Empty;
    public decimal Spent { get; private init; }
    public decimal? SpendingLimit { get; private init; }
    public bool HasSpendingLimit => SpendingLimit is > 0m;
    public string SpentText { get; private init; } = string.Empty;
    public string LimitText { get; private init; } = string.Empty;
    public string RemainderText { get; private init; } = string.Empty;
    public string PercentageText { get; private init; } = string.Empty;
    public double ProgressPercentage { get; private init; }
    public SettingsTagSpendingState SpendingState { get; private init; }

    internal static SettingsTagCardVM Create(Tag tag, decimal spent)
    {
        if (tag.SpendingLimit is not > 0m)
        {
            return new SettingsTagCardVM
            {
                Id = tag.Id,
                Name = tag.Name,
                HexCode = tag.HexCode,
                Spent = spent,
                SpendingLimit = null,
                SpentText = FormatMoney(spent),
                PercentageText = "∞",
                ProgressPercentage = 100d,
                SpendingState = SettingsTagSpendingState.Success
            };
        }

        var limit = tag.SpendingLimit.Value;
        var rawPercentage = spent / limit * 100m;
        var percentage = (int)Math.Round(rawPercentage, MidpointRounding.AwayFromZero);
        var state = rawPercentage < 75m
            ? SettingsTagSpendingState.Success
            : rawPercentage <= 100m
                ? SettingsTagSpendingState.Warning
                : SettingsTagSpendingState.Danger;

        return new SettingsTagCardVM
        {
            Id = tag.Id,
            Name = tag.Name,
            HexCode = tag.HexCode,
            Spent = spent,
            SpendingLimit = limit,
            SpentText = FormatMoney(spent),
            LimitText = $"of {FormatMoney(limit)}",
            RemainderText = $"{FormatMoney(Math.Abs(limit - spent))} {(spent <= limit ? "left" : "over")}",
            PercentageText = $"{percentage}%",
            ProgressPercentage = (double)Math.Clamp(rawPercentage, 0m, 100m),
            SpendingState = state
        };
    }

    private static string FormatMoney(decimal value) =>
        value.ToString("N0", CultureInfo.InvariantCulture);
}
