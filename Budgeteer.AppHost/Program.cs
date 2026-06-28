var builder = DistributedApplication.CreateBuilder(args);

// PostgreSQL for Account Domain event store
var accountsDb = builder.AddPostgres("postgres-accounts")
    .WithDataVolume()
    .AddDatabase("accounts-eventstore");

// PostgreSQL for Budget Domain event store
var budgetDb = builder.AddPostgres("postgres-budget")
    .WithDataVolume()
    .AddDatabase("budget-eventstore");

// Blazor Web App with references to both databases
builder.AddProject<Projects.Budgeteer_Web>("budgeteer-web")
    .WithReference(accountsDb)
    .WithReference(budgetDb);

builder.Build().Run();
