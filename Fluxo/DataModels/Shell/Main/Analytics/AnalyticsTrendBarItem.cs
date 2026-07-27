using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluxo.Core.DTO;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Services.Dialogs;
using Fluxo.Services.Ui;

namespace Fluxo.DataModels.Shell.Main.Analytics;

public sealed record AnalyticsTrendBarItem(
    string Label,
    decimal Value,
    double BarHeightRatio,
    decimal SecondaryValue,
    double SecondaryBarHeightRatio,
    bool HasSecondaryBar,
    bool IsHighlighted,
    bool HideValueText,
    bool RotateLabelVertical,
    bool IsExpenseMode,
    bool IsIncomeMode,
    bool IsSecondaryExpenseMode,
    bool IsSecondaryIncomeMode)
{
    public string ValueText => $"{Value:N0}";
    public string SecondaryValueText => $"{SecondaryValue:N0}";
}
