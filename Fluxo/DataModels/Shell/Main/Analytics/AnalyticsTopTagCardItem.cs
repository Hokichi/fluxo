using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluxo.Core.DTO;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Services.Dialogs;
using Fluxo.Services.Ui;

namespace Fluxo.DataModels.Shell.Main.Analytics;

public sealed record AnalyticsTopTagCardItem(
    string TagName,
    string HexCode,
    decimal Total,
    double ProgressPercent)
{
    public string TotalText => $"${Total:N0}";
}
