using LoreAI.Infrastructure.Persistence;

namespace LoreAI.Worker.Services;

/// <summary>
/// Applique le schéma SQLite au démarrage. Sans cela, la base n'était créée qu'à la première requête
/// utile — donc au premier tick cron : un volume non inscriptible ou un fichier corrompu ne se serait
/// manifesté que quinze minutes après le lancement, dans le catch du job.
/// </summary>
public sealed class DatabaseInitializer : IHostedService
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(SqliteConnectionFactory connectionFactory, ILogger<DatabaseInitializer> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _connectionFactory.InitializeSchemaAsync(cancellationToken);
        _logger.LogInformation("Schéma SQLite vérifié.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
