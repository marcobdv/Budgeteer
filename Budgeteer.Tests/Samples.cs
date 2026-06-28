namespace Budgeteer.Tests;

/// <summary>
/// Sample CSV payloads used across tests. These mirror the files under <c>sample-data/</c>.
/// </summary>
internal static class Samples
{
    // Mirrors sample-data/rabobank-example.csv (first two transactions)
    public const string RabobankCsv =
        "\"IBAN/BBAN\",\"Munt\",\"BIC\",\"Volgnr\",\"Datum\",\"Rentedatum\",\"Bedrag\",\"Saldo na trn\",\"Tegenrekening IBAN/BBAN\",\"Naam tegenpartij\",\"Naam uiteindelijke partij\",\"Naam initiërende partij\",\"BIC tegenpartij\",\"Code\",\"Batch ID\",\"Transactiereferentie\",\"Machtigingskenmerk\",\"Incassant ID\",\"Betalingskenmerk\",\"Omschrijving-1\",\"Omschrijving-2\",\"Omschrijving-3\",\"Reden retour\",\"Oorspr bedrag\",\"Oorspr munt\",\"Koers\"\n" +
        "\"NL11RABO0123456789\",\"EUR\",\"RABONL2U\",\"000000000000000001\",\"2024-01-15\",\"2024-01-15\",\"-12,50\",\"987,50\",\"NL22INGB0009876543\",\"Albert Heijn 1234\",\"\",\"\",\"INGBNL2A\",\"ba\",\"\",\"2024011500001\",\"\",\"\",\"\",\"Boodschappen\",\"AH Filiaal 1234\",\"\",\"\",\"\",\"\",\"\"\n" +
        "\"NL11RABO0123456789\",\"EUR\",\"RABONL2U\",\"000000000000000002\",\"2024-01-16\",\"2024-01-16\",\"+1.500,00\",\"2.487,50\",\"NL33ABNA0001112223\",\"Werkgever BV\",\"\",\"\",\"ABNANL2A\",\"cb\",\"\",\"2024011600002\",\"\",\"\",\"\",\"Salaris januari 2024\",\"\",\"\",\"\",\"\",\"\",\"\"\n";

    // Mirrors sample-data/knab-example.csv (first two transactions)
    public const string KnabCsv =
        "\"Rekeningnummer\";\"Transactiedatum\";\"Valutacode\";\"CreditDebet\";\"Bedrag\";\"Tegenrekeningnummer\";\"Tegenrekeninghouder\";\"Valutadatum\";\"Betaalwijze\";\"Omschrijving\";\"Type betaling\";\"Machtigingsnummer\";\"Incassant ID\";\"Adres\"\n" +
        "\"NL12KNAB0123456789\";\"15-01-2024\";\"EUR\";\"D\";\"45,30\";\"NL22INGB0009876543\";\"Albert Heijn\";\"15-01-2024\";\"Betaalautomaat\";\"Boodschappen AH Amsterdam\";\"Betaalautomaat\";\"\";\"\";\"Damrak 1 Amsterdam\"\n" +
        "\"NL12KNAB0123456789\";\"16-01-2024\";\"EUR\";\"C\";\"2.250,00\";\"NL33ABNA0001112223\";\"Werkgever BV\";\"16-01-2024\";\"Overboeking\";\"Salaris januari 2024\";\"Overboeking\";\"\";\"\";\"\"\n";
}
