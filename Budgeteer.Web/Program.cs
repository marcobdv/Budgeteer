using Marten;
using Budgeteer.Budget.EventHandlers;
using Budgeteer.Budget.Categorization;
using Budgeteer.Accounts.Import;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults (Aspire observability, health checks, etc.)
builder.AddServiceDefaults();

// Add Blazor services
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Configure Marten Event Store (single store for MVP)
var connectionString = builder.Configuration.GetConnectionString("accounts-eventstore")
    ?? "Host=localhost;Database=budgeteer;Username=postgres;Password=postgres";

builder.Services.AddMarten(opts =>
    {
        opts.Connection(connectionString);
        // Streams are keyed by string ids (e.g. account GUIDs as strings),
        // so Marten must use string stream identity rather than its Guid default.
        opts.Events.StreamIdentity = JasperFx.Events.StreamIdentity.AsString;

        // Read models / projections — maintained inline so queries hit documents,
        // not a full event-store replay. These are dedicated, side-effect-free read models
        // (the command-side Account/Expense aggregates keep their factory/business methods).
        opts.Projections.Snapshot<Budgeteer.Accounts.ReadModels.AccountSummary>(
            Marten.Events.Projections.SnapshotLifecycle.Inline);
        opts.Projections.Snapshot<Budgeteer.Budget.ReadModels.ExpenseView>(
            Marten.Events.Projections.SnapshotLifecycle.Inline);
        opts.Projections.Snapshot<Budgeteer.Budget.Domain.Income>(
            Marten.Events.Projections.SnapshotLifecycle.Inline);
        opts.Projections.Add(
            new Budgeteer.Accounts.ReadModels.TransactionViewProjection(),
            JasperFx.Events.Projections.ProjectionLifecycle.Inline);
    })
    .UseLightweightSessions();

// Register event handlers
builder.Services.AddScoped<TransactionEventHandler>();

// Smart categorization
builder.Services.AddScoped<TransactionCategorizer>();
builder.Services.AddScoped<Budgeteer.Web.Services.BudgetService>();
builder.Services.AddScoped<Budgeteer.Web.Services.BudgetAllocationService>();
builder.Services.AddScoped<Budgeteer.Web.Services.TransferDetectionService>();
builder.Services.AddScoped<Budgeteer.Web.Services.SavingGoalService>();
builder.Services.AddScoped<Budgeteer.Web.Services.LedgerService>();
builder.Services.AddScoped<Budgeteer.Web.Services.TransactionQueryService>();
builder.Services.AddHostedService<Budgeteer.Web.Services.CategorizationSeeder>();

// CSV bank-statement import (stateless, so a singleton is fine)
builder.Services.AddSingleton<BankStatementImporter>();
builder.Services.AddScoped<Budgeteer.Web.Services.BankImportService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapDefaultEndpoints();

// CSV export of all transactions (downloads as a file).
app.MapGet("/export/transactions.csv", async (Budgeteer.Web.Services.TransactionQueryService query) =>
{
    var rows = await query.LoadAllAsync();
    var csv = Budgeteer.Web.Services.TransactionCsvExporter.ToCsv(rows);
    var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
    return Results.File(bytes, "text/csv", "budgeteer-transactions.csv");
});

app.MapRazorComponents<Budgeteer.Web.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
