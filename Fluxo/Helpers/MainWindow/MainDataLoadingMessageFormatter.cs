using System.Globalization;
using Fluxo.Core.Enums;

namespace Fluxo.Helpers.MainWindow;

internal static class MainDataLoadingMessageFormatter
{
    private const string DayFormat = "MMMM d";
    private const string MonthFormat = "MMMM";

    public static string Build(MainContentViewMode viewMode, DateTime selectedDate)
    {
        if (viewMode == MainContentViewMode.AllTime)
            return "Loading all-time data";

        if (viewMode == MainContentViewMode.AllocationPeriod)
            return "Loading allocation period data";

        var range = DateRangeResolver.Resolve(selectedDate, viewMode);
        return viewMode switch
        {
            MainContentViewMode.Daily =>
                $"Loading data for {range.From.ToString(DayFormat, CultureInfo.InvariantCulture)}",
            MainContentViewMode.Weekly =>
                $"Loading data from {range.From.ToString(DayFormat, CultureInfo.InvariantCulture)} to {range.To.ToString(DayFormat, CultureInfo.InvariantCulture)}",
            MainContentViewMode.Monthly =>
                $"Loading data for {range.From.ToString(MonthFormat, CultureInfo.InvariantCulture)}",
            _ => $"Loading data for {selectedDate.ToString(DayFormat, CultureInfo.InvariantCulture)}"
        };
    }
}
