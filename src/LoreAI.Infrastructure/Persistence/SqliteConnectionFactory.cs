using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace LoreAI.Infrastructure.Persistence;

/// <summary>
/// Ouvre des connexions SQLite et applique le script de schéma embarqué une seule fois par process.
/// </summary>
public sealed class SqliteConnectionFactory : IDisposable
{
    private const string SchemaResourceName = "LoreAI.Infrastructure.Persistence.Migrations.0001_InitialSchema.sql";

    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly string _connectionString;
    private bool _schemaEnsured;

    public SqliteConnectionFactory(IOptions<SqliteOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        return connection;
    }

    /// <summary>
    /// Applique le schéma sans avoir besoin d'une requête métier. Appelé au démarrage par l'hôte, pour
    /// qu'une base illisible ou un disque en lecture seule se manifeste tout de suite et non au premier
    /// cycle, quinze minutes plus tard. Le chemin paresseux ci-dessus reste en place comme filet.
    /// </summary>
    public async Task InitializeSchemaAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
    }

    /// <summary>Libère le verrou d'initialisation. La fabrique est un singleton, donc disposée à l'arrêt de l'hôte.</summary>
    public void Dispose() => _initLock.Dispose();

    private async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (_schemaEnsured)
        {
            return;
        }

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_schemaEnsured)
            {
                return;
            }

            var assembly = typeof(SqliteConnectionFactory).Assembly;
            await using var stream = assembly.GetManifestResourceStream(SchemaResourceName)
                ?? throw new InvalidOperationException($"Ressource embarquée introuvable : {SchemaResourceName}");
            using var reader = new StreamReader(stream);
            var schemaSql = await reader.ReadToEndAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = schemaSql;
            await command.ExecuteNonQueryAsync(cancellationToken);

            _schemaEnsured = true;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
