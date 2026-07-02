using System.Globalization;
using System.Text;

namespace Budgeteer.Web.Services;

/// <summary>Renders transaction rows to CSV for download.</summary>
public static class TransactionCsvExporter
{
    public static string ToCsv(IEnumerable<TransactionRow> rows)
    {
        var sb = new StringBuilder();
        sb.Append("Date,Account,Description,Payee,Category,Amount,Type\r\n");
        foreach (var r in rows.OrderByDescending(r => r.Date))
        {
            var type = r.IsTransfer ? "Transfer" : r.Amount >= 0 ? "Income" : "Expense";
            var category = r.IsTransfer ? "Transfer" : (r.Category ?? "");
            sb.Append(r.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
              .Append(EscapeText(r.AccountName)).Append(',')
              .Append(EscapeText(r.Description)).Append(',')
              .Append(EscapeText(r.Payee ?? "")).Append(',')
              .Append(EscapeText(category)).Append(',')
              .Append(r.Amount.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
              .Append(type)
              .Append("\r\n");
        }
        return sb.ToString();
    }

    // Characters that make a spreadsheet interpret a cell as a formula. Descriptions and
    // payees come from imported bank statements, i.e. from counterparties, so a payment
    // description like =HYPERLINK(...) would otherwise execute when the export is opened.
    private static readonly char[] FormulaPrefixes = { '=', '+', '-', '@', '\t', '\r' };

    /// <summary>Escapes a free-text field: RFC-4180 quoting plus formula-injection guarding.</summary>
    private static string EscapeText(string value)
    {
        if (value.Length > 0 && Array.IndexOf(FormulaPrefixes, value[0]) >= 0)
            value = "'" + value;
        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
            return value;
        return '"' + value.Replace("\"", "\"\"") + '"';
    }
}
