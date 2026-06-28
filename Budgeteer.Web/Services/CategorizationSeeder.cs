using Budgeteer.Budget.Categorization;

namespace Budgeteer.Web.Services;

/// <summary>
/// Seeds the default categorization rules once at startup. Idempotent and resilient:
/// if the database isn't reachable yet it logs and moves on rather than crashing the app.
/// </summary>
public sealed class CategorizationSeeder : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<CategorizationSeeder> _logger;

    public CategorizationSeeder(IServiceProvider services, ILogger<CategorizationSeeder> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _services.CreateScope();
            var categorizer = scope.ServiceProvider.GetRequiredService<TransactionCategorizer>();
            await categorizer.SeedDefaultsAsync();
            _logger.LogInformation("Default categorization rules ensured.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not seed categorization rules at startup; they will be seeded on first use.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
