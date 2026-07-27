using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluxo.Core.DTO;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Services.Dialogs;
using Fluxo.Services.Ui;

namespace Fluxo.DataModels.Shell.Main.Analytics;

public sealed record AnalyticsGoalCardItem(
    string Name,
    decimal CurrentAmount,
    decimal TargetAmount,
    DateTime CreatedOn,
    DateTime? SavingEndDate)
{
    public double ProgressPercent =>
        TargetAmount <= 0m ? 0d : Math.Clamp((double)(CurrentAmount / TargetAmount * 100m), 0d, 100d);
}
