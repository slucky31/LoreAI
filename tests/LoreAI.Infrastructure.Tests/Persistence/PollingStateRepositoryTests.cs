using Microsoft.EntityFrameworkCore;
using LoreAI.Core.Enums;
using LoreAI.Core.Models;
using LoreAI.Infrastructure.Persistence;

namespace LoreAI.Infrastructure.Tests.Persistence;

[Collection("Postgres")]
public class PollingStateRepositoryTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly PollingStateRepository _repository;

    public PollingStateRepositoryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _repository = new PollingStateRepository(fixture.ContextFactory, new PostgresSchemaGuard(fixture.ContextFactory));
    }

    public async ValueTask InitializeAsync()
    {
        await using var context = _fixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"PollingStates\" RESTART IDENTITY CASCADE");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task GetAsync_WithoutPriorState_ReturnsInitial()
    {
        var state = await _repository.GetAsync(SourceType.Raindrop, TestContext.Current.CancellationToken);

        Assert.Equal(PollingState.Initial(SourceType.Raindrop), state);
    }

    [Fact]
    public async Task UpdateAsync_ThenGetAsync_RoundTripsValues()
    {
        var expected = new PollingState(
            SourceType.Raindrop,
            "123",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-01-02T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));

        await _repository.UpdateAsync(expected, TestContext.Current.CancellationToken);
        var actual = await _repository.GetAsync(SourceType.Raindrop, TestContext.Current.CancellationToken);

        Assert.Equal(expected.LastSourceItemId, actual.LastSourceItemId);
        Assert.Equal(expected.LastCreatedUtc, actual.LastCreatedUtc);
        Assert.Equal(expected.UpdatedAtUtc, actual.UpdatedAtUtc);
    }

    [Fact]
    public async Task UpdateAsync_CalledTwice_OverwritesSingletonRow()
    {
        await _repository.UpdateAsync(new PollingState(SourceType.Raindrop, "1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), TestContext.Current.CancellationToken);
        var second = new PollingState(SourceType.Raindrop, "2", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await _repository.UpdateAsync(second, TestContext.Current.CancellationToken);

        var actual = await _repository.GetAsync(SourceType.Raindrop, TestContext.Current.CancellationToken);

        Assert.Equal("2", actual.LastSourceItemId);
    }

    [Fact]
    public async Task GetAsync_DifferentSources_HaveIndependentCursors()
    {
        await _repository.UpdateAsync(new PollingState(SourceType.Raindrop, "1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), TestContext.Current.CancellationToken);

        var raindrop = await _repository.GetAsync(SourceType.Raindrop, TestContext.Current.CancellationToken);
        var feed = await _repository.GetAsync(SourceType.Feed, TestContext.Current.CancellationToken);

        Assert.Equal("1", raindrop.LastSourceItemId);
        Assert.Equal(PollingState.Initial(SourceType.Feed), feed);
    }
}
