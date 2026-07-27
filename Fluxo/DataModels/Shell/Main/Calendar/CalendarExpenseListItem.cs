using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluxo.Core.DTO;
using Fluxo.Core.Interfaces.Services;

namespace Fluxo.DataModels.Shell.Main.Calendar;

public sealed record CalendarExpenseListItem(
    string Name,
    decimal Amount,
    string AccountName,
    string? TagName)
{
    public string AmountText => $"{Amount:N0}";
}
