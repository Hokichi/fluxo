using System.Globalization;
using Fluxo.ViewModels.Shell.Main;
using System.Text;

namespace Fluxo.Helpers.MainWindow;

public static class LedgerCsvExport
{
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    public static string BuildFileName(DateTime timestamp)
    {
        return $"fluxo_Ledger_{timestamp:yyyyMMdd-hhmmss}.csv";
    }

    public static byte[] BuildBytes(IEnumerable<LedgerTransactionItemVM> transactions)
    {
        ArgumentNullException.ThrowIfNull(transactions);

        var builder = new StringBuilder();
        AppendCsvRow(builder, ["Date", "Amount", "Type", "Tag"]);

        foreach (var transaction in transactions)
        {
            var signedAmount = transaction.Kind == LedgerTransactionKind.Income
                ? transaction.Amount
                : -transaction.Amount;

            AppendCsvRow(builder,
            [
                transaction.OccurredOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                signedAmount.ToString("0.00", CultureInfo.InvariantCulture),
                transaction.Kind == LedgerTransactionKind.Income ? "Income" : "Expense",
                transaction.Kind == LedgerTransactionKind.Expense ? transaction.TagName : string.Empty
            ]);
        }

        return Utf8WithBom.GetPreamble()
            .Concat(Utf8WithBom.GetBytes(builder.ToString()))
            .ToArray();
    }

    private static void AppendCsvRow(StringBuilder builder, IReadOnlyList<string> values)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
                builder.Append(',');

            builder.Append(Escape(values[i]));
        }

        builder.AppendLine();
    }

    private static string Escape(string value)
    {
        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
            return value;

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
