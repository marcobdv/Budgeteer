using Budgeteer.Accounts.Domain;
using Budgeteer.Accounts.ReadModels;
using Budgeteer.Shared.Events.Accounts;
using Budgeteer.Web.Services;
using Marten;
using Npgsql;
using Xunit;

namespace Budgeteer.Tests;

/// <summary>
/// End-to-end transfer detection: appends a transfer pair across two accounts, lets the inline
/// projections build the read models, then runs the detection service and checks persistence.
/// </summary>
public class TransferIntegrationTests
{
    private static DocumentStore CreateStore() => DocumentStore.For(opts =>
    {
        opts.Connection(TestPostgres.ConnectionString);
        opts.DatabaseSchemaName = "transfer_it";
        opts.Events.StreamIdentity = JasperFx.Events.StreamIdentity.AsString;
        opts.Projections.Snapshot<AccountSummary>(Marten.Events.Projections.SnapshotLifecycle.Inline);
        opts.Projections.Add(new TransactionViewProjection(),
            JasperFx.Events.Projections.ProjectionLifecycle.Inline);
    });

    [SkippableFact]
    public async Task Detects_and_persists_a_transfer_between_two_accounts()
    {
        TestPostgres.SkipUnlessAvailable();

        await using var store = CreateStore();
        await store.Advanced.Clean.DeleteAllEventDataAsync();
        await store.Advanced.Clean.DeleteAllDocumentsAsync();

        var a = Account.Create("Checking", "Checking", 0m, "NL11AAAA0000000001");
        var b = Account.Create("Savings", "Savings", 0m, "NL22BBBB0000000002");

        await using (var s = store.LightweightSession())
        {
            s.Events.StartStream<Account>(a.AccountId, a);
            s.Events.StartStream<Account>(b.AccountId, b);
            await s.SaveChangesAsync();
        }

        // Transfer of 100 from A (to B's IBAN) and into B (from A's IBAN).
        var outLeg = new TransactionRecorded(System.Guid.NewGuid().ToString(), a.AccountId,
            new System.DateTime(2024, 3, 1), "Naar spaarrekening", -100m, "Savings",
            System.DateTime.UtcNow, "x1", "NL22BBBB0000000002");
        var inLeg = new TransactionRecorded(System.Guid.NewGuid().ToString(), b.AccountId,
            new System.DateTime(2024, 3, 2), "Van betaalrekening", 100m, "Checking",
            System.DateTime.UtcNow, "x2", "NL11AAAA0000000001");

        await using (var s = store.LightweightSession())
        {
            s.Events.Append(a.AccountId, outLeg);
            s.Events.Append(b.AccountId, inLeg);
            await s.SaveChangesAsync();
        }

        var detector = new TransferDetectionService(store);
        var found = await detector.DetectAsync();
        Assert.Equal(1, found);

        // Idempotent: a second run finds nothing new.
        Assert.Equal(0, await detector.DetectAsync());

        var ids = await detector.GetTransferTransactionIdsAsync();
        Assert.Contains(outLeg.TransactionId, ids);
        Assert.Contains(inLeg.TransactionId, ids);
    }
}
