namespace Budgeteer.Accounts.Import;

/// <summary>
/// Parses Rabobank "comma-separated" (kommagescheiden) CSV exports.
///
/// Rabobank exports are comma-separated and use a comma as the decimal separator
/// (fields are quoted). The amount column ("Bedrag") is already signed
/// (e.g. "-12,50" / "+34,00"). Dates are yyyy-MM-dd. The description is split
/// across "Omschrijving-1", "Omschrijving-2" and "Omschrijving-3".
///
/// Typical header (subset):
/// "IBAN/BBAN","Munt","BIC","Volgnr","Datum","Rentedatum","Bedrag","Saldo na trn",
/// "Tegenrekening IBAN/BBAN","Naam tegenpartij",...,"Omschrijving-1","Omschrijving-2","Omschrijving-3",...
/// </summary>
public sealed class RabobankCsvParser : IBankStatementParser
{
    public BankFormat Format => BankFormat.Rabobank;

    public bool CanParse(string headerLine)
    {
        if (string.IsNullOrWhiteSpace(headerLine))
            return false;
        var h = headerLine.ToLowerInvariant();
        return h.Contains("iban/bban")
            || (h.Contains("naam tegenpartij") && h.Contains("saldo na trn"));
    }

    public IReadOnlyList<BankMutation> Parse(Stream csv)
    {
        var rows = CsvParsingHelpers.ReadRows(csv, delimiter: ",");
        var mutations = new List<BankMutation>(rows.Count);

        foreach (var row in rows)
        {
            var dateRaw = row.Field("Datum", "Rentedatum");
            var amountRaw = row.Field("Bedrag");
            if (string.IsNullOrWhiteSpace(dateRaw) || string.IsNullOrWhiteSpace(amountRaw))
                continue;

            try
            {
                var amount = CsvParsingHelpers.ParseAmount(amountRaw);

                var accountIban = row.Field("IBAN/BBAN", "IBAN");
                var counterpartyIban = row.Field("Tegenrekening IBAN/BBAN", "Tegenrekening");
                var counterpartyName = row.Field("Naam tegenpartij", "Naam uiteindelijke partij");
                var currency = row.Field("Munt", "Valuta");
                if (string.IsNullOrWhiteSpace(currency))
                    currency = "EUR";

                var description = string.Join(" ", new[]
                    {
                        row.Field("Omschrijving-1"),
                        row.Field("Omschrijving-2"),
                        row.Field("Omschrijving-3")
                    }
                    .Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
                if (string.IsNullOrWhiteSpace(description))
                    description = row.Field("Omschrijving", "Betalingskenmerk");

                var volgnr = row.Field("Volgnr");
                var date = CsvParsingHelpers.ParseDate(dateRaw);

                mutations.Add(new BankMutation
                {
                    AccountIban = Iban.From(accountIban),
                    Date = date,
                    Amount = amount,
                    Currency = currency,
                    Description = description,
                    CounterpartyName = string.IsNullOrWhiteSpace(counterpartyName) ? null : counterpartyName,
                    CounterpartyIban = Iban.From(counterpartyIban),
                    BalanceAfter = CsvParsingHelpers.ParseOptionalAmount(row.Field("Saldo na trn", "Saldo")),
                    DedupKey = CsvParsingHelpers.MakeDedupKey(
                        "rabobank", accountIban, dateRaw, amountRaw, volgnr, counterpartyIban, description)
                });
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            {
                // Skip a single malformed row (bad number/date or an out-of-range amount)
                // rather than aborting the whole import.
                continue;
            }
        }

        return mutations;
    }
}
