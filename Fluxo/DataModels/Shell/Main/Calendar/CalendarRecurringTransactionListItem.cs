using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluxo.Core.DTO;
using Fluxo.Core.Interfaces.Services;

namespace Fluxo.DataModels.Shell.Main.Calendar;

public sealed record CalendarRecurringTransactionListItem(
    string Name,
    decimal Amount,
    string TypeText,
    string SourceName)
{
    public string AmountText => $"{Amount:N0}";
}
