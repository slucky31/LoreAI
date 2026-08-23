using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using LoreAI.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace LoreAI.Infrastructure.Tests.Persistence;

/// <summary>
/// Un seul conteneur Postgres pour toute la collection de tests de persistance (démarrage lent sinon) :
/// remplace l'isolation qu'assurait un fichier SQLite par test. Chaque classe de test tronque les tables
/// dans son propre <c>IAsyncLifetime.InitializeAsync</c> pour rester indépendante des autres.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public string ConnectionString => _container?.GetConnectionString()
        ?? throw new InvalidOperationException("Le conteneur Postgres n'est pas encore démarré.");

    public IDbContextFactory<LoreAiDbContext> ContextFactory { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        // Version majeure epinglee (coherent avec l'instance mutualisee du Pi, ADR 0009 : "version
        // majeure epinglee" - un desaccord de version entre les tests et la prod serait le pire moment
        // pour le decouvrir).
        _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<LoreAiDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        ContextFactory = new PooledDbContextFactory<LoreAiDbContext>(options);

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public LoreAiDbContext CreateContext() => ContextFactory.CreateDbContext();

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}

[CollectionDefinition("Postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
