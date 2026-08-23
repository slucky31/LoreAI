using System.Globalization;
using Microsoft.EntityFrameworkCore;
using LoreAI.Core.Enums;
using LoreAI.Core.Models;
using LoreAI.Infrastructure.Persistence;

namespace LoreAI.Infrastructure.Tests.Persistence;

[Collection("Postgres")]
public class LibraryIndexStateRepositoryTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly LibraryIndexStateRepository _repository;

    public LibraryIndexStateRepositoryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _repository = new LibraryIndexStateRepository(fixture.ContextFactory, new PostgresSchemaGuard(fixture.ContextFactory));
    }

    public async ValueTask InitializeAsync()
    {
        await using var context = _fixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"LibraryIndexStates\" RESTART IDENTITY CASCADE");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task GetAsync_WithoutPriorState_ReturnsInitial()
    {
        var state = await _repository.GetAsync(SourceType.Raindrop, TestContext.Current.CancellationToken);

        Assert.Equal(LibraryIndexState.Initial(SourceType.Raindrop), state);
    }

    [Fact]
    public async Task UpdateAsync_ThenGetAsync_RoundTripsValues()
    {
        var expected = new LibraryIndexState(
            SourceType.Raindrop,
            3,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z", CultureInfo.InvariantCulture),
            null,
            DateTimeOffset.Parse("2026-01-02T00:00:00Z", CultureInfo.InvariantCulture));

        await _repository.UpdateAsync(expected, TestContext.Current.CancellationToken);
        var actual = await _repository.GetAsync(SourceType.Raindrop, TestContext.Current.CancellationToken);

        Assert.Equal(expected.ResumePage, actual.ResumePage);
        Assert.Equal(expected.LastFullPassStartedUtc, actual.LastFullPassStartedUtc);
        Assert.Equal(expected.LastFullPassCompletedUtc, actual.LastFullPassCompletedUtc);
        Assert.Equal(expected.UpdatedAtUtc, actual.UpdatedAtUtc);
    }

    /// <summary>Fin de passe : le job persiste <c>ResumePage = null</c> — vérifie que la ligne existante est bien mise à jour, pas seulement créée.</summary>
    [Fact]
    public async Task UpdateAsync_CompletingPass_ClearsResumePage()
    {
        await _repository.UpdateAsync(
            new LibraryIndexState(SourceType.Raindrop, 5, DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow),
            TestContext.Current.CancellationToken);

        var completed = new LibraryIndexState(SourceType.Raindrop, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await _repository.UpdateAsync(completed, TestContext.Current.CancellationToken);

        var actual = await _repository.GetAsync(SourceType.Raindrop, TestContext.Current.CancellationToken);

        Assert.Null(actual.ResumePage);
        Assert.NotNull(actual.LastFullPassCompletedUtc);
    }

    [Fact]
    public async Task GetAsync_DifferentSources_HaveIndependentCursors()
    {
        await _repository.UpdateAsync(
            new LibraryIndexState(SourceType.Raindrop, 2, DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow),
            TestContext.Current.CancellationToken);

        var raindrop = await _repository.GetAsync(SourceType.Raindrop, TestContext.Current.CancellationToken);
        var feed = await _repository.GetAsync(SourceType.Feed, TestContext.Current.CancellationToken);

        Assert.Equal(2, raindrop.ResumePage);
        Assert.Equal(LibraryIndexState.Initial(SourceType.Feed), feed);
    }
}
