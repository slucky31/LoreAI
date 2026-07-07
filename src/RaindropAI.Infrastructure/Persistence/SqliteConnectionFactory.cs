using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace RaindropAI.Infrastructure.Persistence;

/// <summary>
/// Ouvre des connexions SQLite et applique le script de schéma embarqué une seule fois par process.
/// </summary>
public sealed class SqliteConnectionFactory
{
    private const string SchemaResourceName = "RaindropAI.Infrastructure.Persistence.Migrations.0001_InitialSchema.sql";

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

            var command = connection.CreateCommand();
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
