var builder = DistributedApplication.CreateBuilder(args);

// PostgreSQL hosting the single Marten event store (both domains share it — see README).
var accountsDb = builder.AddPostgres("postgres-accounts")
    .WithDataVolume()
    .AddDatabase("accounts-eventstore");

builder.AddProject<Projects.Budgeteer_Web>("budgeteer-web")
    .WithReference(accountsDb);

builder.Build().Run();
