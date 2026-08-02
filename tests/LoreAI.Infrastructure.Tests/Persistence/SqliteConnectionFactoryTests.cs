using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using LoreAI.Infrastructure.Persistence;

namespace LoreAI.Infrastructure.Tests.Persistence;

public class SqliteConnectionFactoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteOptions _options;

    public SqliteConnectionFactoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"loreai-schema-{Guid.NewGuid():N}.db");
        _options = new SqliteOptions { ConnectionString = $"Data Source={_dbPath}" };
    }

    [Fact]
    public async Task InitializeSchemaAsync_CreatesTheSchemaWithoutAnyBusinessQuery()
    {
        var factory = new SqliteConnectionFactory(Options.Create(_options));

        await factory.InitializeSchemaAsync(TestContext.Current.CancellationToken);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        var tables = (await connection.QueryAsync<string>(
            "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name")).ToList();

        Assert.Contains("Articles", tables);
        Assert.Contains("PollingState", tables);
        Assert.Contains("SchemaVersion", tables);
    }

    /// <summary>
    /// Le script est rejoué à chaque démarrage : la ligne de version ne doit pas s'empiler, et une base
    /// déjà peuplée doit survivre intacte.
    /// </summary>
    [Fact]
    public async Task InitializeSchemaAsync_IsIdempotentAcrossProcesses()
    {
        await new SqliteConnectionFactory(Options.Create(_options)).InitializeSchemaAsync(TestContext.Current.CancellationToken);

        await using (var seed = new SqliteConnection(_options.ConnectionString))
        {
            await seed.ExecuteAsync(
                "INSERT INTO PollingState (Id, LastRaindropId, LastCreatedUtc, UpdatedAtUtc) VALUES (1, 42, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')");
        }

        // Nouvelle fabrique = nouveau process du point de vue du verrou d'initialisation.
        await new SqliteConnectionFactory(Options.Create(_options)).InitializeSchemaAsync(TestContext.Current.CancellationToken);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM SchemaVersion"));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT Version FROM SchemaVersion"));
        Assert.Equal(42, await connection.ExecuteScalarAsync<long>("SELECT LastRaindropId FROM PollingState WHERE Id = 1"));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }
}
