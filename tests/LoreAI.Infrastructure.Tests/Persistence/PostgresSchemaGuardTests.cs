using Microsoft.EntityFrameworkCore;
using LoreAI.Infrastructure.Persistence;

namespace LoreAI.Infrastructure.Tests.Persistence;

[Collection("Postgres")]
public class PostgresSchemaGuardTests
{
    private readonly PostgresFixture _fixture;

    public PostgresSchemaGuardTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task EnsureMigratedAsync_CreatesTheSchema()
    {
        var guard = new PostgresSchemaGuard(_fixture.ContextFactory);

        await guard.EnsureMigratedAsync(TestContext.Current.CancellationToken);

        await using var context = _fixture.CreateContext();
        var applied = await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(applied);
    }

    /// <summary>
    /// Les migrations EF Core sont rejouables sans effet : un deuxième appel (nouveau process du point
    /// de vue du verrou d'initialisation) ne doit ni échouer ni dupliquer quoi que ce soit.
    /// </summary>
    [Fact]
    public async Task EnsureMigratedAsync_IsIdempotentAcrossInstances()
    {
        await new PostgresSchemaGuard(_fixture.ContextFactory).EnsureMigratedAsync(TestContext.Current.CancellationToken);
        await new PostgresSchemaGuard(_fixture.ContextFactory).EnsureMigratedAsync(TestContext.Current.CancellationToken);

        await using var context = _fixture.CreateContext();
        Assert.True(await context.Database.CanConnectAsync(TestContext.Current.CancellationToken));
    }
}
