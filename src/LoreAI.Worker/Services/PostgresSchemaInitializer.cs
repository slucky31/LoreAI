using LoreAI.Infrastructure.Persistence;

namespace LoreAI.Worker.Services;

/// <summary>
/// Applique les migrations EF Core au démarrage. Sans cela, le schéma n'était vérifié qu'à la première
/// requête utile — donc au premier tick cron : un Postgres injoignable ou un schéma désynchronisé ne se
/// serait manifesté que quinze minutes après le lancement, dans le catch du job.
/// </summary>
/// <remarks>
/// L'échec est journalisé, pas relancé : conformément à l'ADR 0009, l'instance Postgres n'appartient pas
/// à LoreAI (pas de <c>depends_on</c>) et son indisponibilité au démarrage est une panne transitoire, pas
/// fatale. <see cref="PostgresSchemaGuard"/> retente à chaque appel ultérieur d'un repository.
/// </remarks>
public sealed class PostgresSchemaInitializer : IHostedService
{
    private readonly PostgresSchemaGuard _schemaGuard;
    private readonly ILogger<PostgresSchemaInitializer> _logger;

    public PostgresSchemaInitializer(PostgresSchemaGuard schemaGuard, ILogger<PostgresSchemaInitializer> logger)
    {
        _schemaGuard = schemaGuard;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _schemaGuard.EnsureMigratedAsync(cancellationToken);
            _logger.LogInformation("Schéma PostgreSQL vérifié.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Postgres injoignable au démarrage ; nouvelle tentative au premier cycle.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
