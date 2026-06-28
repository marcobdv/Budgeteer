using System.Globalization;
using System.Text;

namespace Budgeteer.Web.Services;

/// <summary>Renders transaction rows to CSV for download.</summary>
public static class TransactionCsvExporter
{
    public static string ToCsv(IEnumerable<TransactionRow> rows)
    {
        var sb = new StringBuilder();
        sb.Append("Date,Account,Description,Payee,Category,Amount,Type\n");
        foreach (var r in rows.OrderByDescending(r => r.Date))
        {
            var type = r.IsTransfer ? "Transfer" : r.Amount >= 0 ? "Income" : "Expense";
            var category = r.IsTransfer ? "Transfer" : (r.Category ?? "");
            sb.Append(r.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
              .Append(Escape(r.AccountName)).Append(',')
              .Append(Escape(r.Description)).Append(',')
              .Append(Escape(r.Payee ?? "")).Append(',')
              .Append(Escape(category)).Append(',')
              .Append(r.Amount.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
              .Append(type)
              .Append('\n');
        }
        return sb.ToString();
    }

    // RFC-4180 quoting: wrap in quotes when the field contains a comma, quote or newline.
    private static string Escape(string value)
    {
        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
            return value;
        return '"' + value.Replace("\"", "\"\"") + '"';
    }
}
