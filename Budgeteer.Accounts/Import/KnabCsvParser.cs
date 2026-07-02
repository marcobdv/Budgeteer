namespace Budgeteer.Accounts.Import;

/// <summary>
/// Parses KNAB bank CSV exports.
///
/// KNAB exports are semicolon-separated and use a comma as the decimal separator.
/// The amount column ("Bedrag") is unsigned; the sign is derived from the
/// "CreditDebet" column (C = credit/income, D = debit/expense). Dates are dd-MM-yyyy.
///
/// Typical header:
/// Rekeningnummer;Transactiedatum;Valutacode;CreditDebet;Bedrag;Tegenrekeningnummer;
/// Tegenrekeninghouder;Valutadatum;Betaalwijze;Omschrijving;Type betaling;
/// Machtigingsnummer;Incassant ID;Adres
/// </summary>
public sealed class KnabCsvParser : IBankStatementParser
{
    public BankFormat Format => BankFormat.Knab;

    public bool CanParse(string headerLine)
    {
        if (string.IsNullOrWhiteSpace(headerLine))
            return false;
        var h = headerLine.ToLowerInvariant();
        // KNAB is the only supported format combining these column names + semicolons.
        return h.Contains("creditdebet")
            || (h.Contains("rekeningnummer") && h.Contains("transactiedatum") && h.Contains(";"));
    }

    public BankParseResult Parse(Stream csv)
    {
        var rows = CsvParsingHelpers.ReadRows(csv, delimiter: ";");
        var mutations = new List<BankMutation>(rows.Count);
        int skipped = 0;
        var skipSamples = new List<string>();
        // KNAB exports carry no per-row sequence number (unlike Rabobank's Volgnr), so two
        // genuinely identical same-day rows would otherwise collapse to one dedup key and the
        // second be skipped as a "duplicate". Disambiguate by occurrence within the file; the
        // ordering is stable, so re-importing the same export still produces the same keys.
        var dedupSeen = new Dictionary<string, int>();

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var dateRaw = row.Field("Transactiedatum", "Datum");
            var amountRaw = row.Field("Bedrag", "Transactiebedrag");
            if (string.IsNullOrWhiteSpace(dateRaw) || string.IsNullOrWhiteSpace(amountRaw))
            {
                RecordSkip(ref skipped, skipSamples, rowIndex, "empty date or amount");
                continue;
            }

            try
            {
                var amount = CsvParsingHelpers.ParseAmount(amountRaw);
                var creditDebet = row.Field("CreditDebet", "Credit/Debet", "Bij/Af").ToUpperInvariant();
                if (creditDebet.StartsWith("D") || creditDebet.StartsWith("AF"))
                    amount = -Math.Abs(amount);
                else if (creditDebet.StartsWith("C") || creditDebet.StartsWith("BIJ"))
                    amount = Math.Abs(amount);
                // If no credit/debet indicator, trust the sign already on the amount.

                // "Tenaamstelling" (the account holder's *name*) is deliberately not a
                // fallback here — it would masquerade as an IBAN and drive account matching.
                var accountIban = row.Field("Rekeningnummer", "IBAN");
                var counterpartyIban = row.Field("Tegenrekeningnummer", "Tegenrekening");
                var counterpartyName = row.Field("Tegenrekeninghouder", "Naam tegenpartij", "Naam");
                var description = row.Field("Omschrijving", "Omschrijving-1", "Mededelingen");
                var currency = row.Field("Valutacode", "Valuta");
                if (string.IsNullOrWhiteSpace(currency))
                    currency = "EUR";

                var date = CsvParsingHelpers.ParseDate(dateRaw);

                var baseKey = CsvParsingHelpers.MakeDedupKey(
                    "knab", accountIban, dateRaw, amountRaw, creditDebet, counterpartyIban, description);
                var occurrence = dedupSeen.TryGetValue(baseKey, out var seen) ? seen : 0;
                dedupSeen[baseKey] = occurrence + 1;
                var dedupKey = occurrence == 0
                    ? baseKey
                    : CsvParsingHelpers.MakeDedupKey(baseKey, occurrence.ToString());

                mutations.Add(new BankMutation
                {
                    AccountIban = Iban.From(accountIban),
                    Date = date,
                    Amount = amount,
                    Currency = currency,
                    Description = description,
                    CounterpartyName = string.IsNullOrWhiteSpace(counterpartyName) ? null : counterpartyName,
                    CounterpartyIban = Iban.From(counterpartyIban),
                    BalanceAfter = CsvParsingHelpers.ParseOptionalAmount(row.Field("Saldo", "Saldo na trn")),
                    DedupKey = dedupKey
                });
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            {
                // Skip a single malformed row (bad number/date or an out-of-range amount)
                // rather than aborting the whole import — but count it, so the UI can warn.
                RecordSkip(ref skipped, skipSamples, rowIndex, ex.Message);
            }
        }

        return new BankParseResult(mutations, skipped, skipSamples);
    }

    private static void RecordSkip(ref int skipped, List<string> samples, int rowIndex, string reason)
    {
        skipped++;
        if (samples.Count < 3)
            samples.Add($"data row {rowIndex + 1}: {reason}");
    }
}
