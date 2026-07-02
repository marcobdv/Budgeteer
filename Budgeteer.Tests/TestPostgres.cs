using Npgsql;
using Xunit;

namespace Budgeteer.Tests;

/// <summary>
/// Central PostgreSQL configuration for the integration tests, so the connection string lives
/// in exactly one place and can be pointed elsewhere via <c>BUDGETEER_TEST_PG</c>.
///
/// By default, tests skip when no server is reachable — convenient on a dev machine without
/// Docker. Set <c>BUDGETEER_REQUIRE_PG=1</c> (as CI should) to turn that skip into a failure;
/// otherwise a CI runner without Postgres silently reports green with zero coverage of the
/// projection/import/ledger guarantees.
/// </summary>
internal static class TestPostgres
{
    public static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("BUDGETEER_TEST_PG")
        ?? "Host=localhost;Port=5432;Database=budgeteer;Username=postgres;Password=postgres;Timeout=3;Command Timeout=10";

    private static readonly Lazy<bool> Reachable = new(() =>
    {
        try
        {
            using var conn = new NpgsqlConnection(ConnectionString);
            conn.Open();
            return true;
        }
        catch
        {
            return false;
        }
    });

    /// <summary>Skips the calling test when Postgres is absent, or fails when it is required.</summary>
    public static void SkipUnlessAvailable()
    {
        if (Reachable.Value)
            return;
        if (Environment.GetEnvironmentVariable("BUDGETEER_REQUIRE_PG") == "1")
            throw new InvalidOperationException(
                "BUDGETEER_REQUIRE_PG=1 but PostgreSQL is not reachable — integration tests must run.");
        Skip.If(true, "Local PostgreSQL not available.");
    }
}
