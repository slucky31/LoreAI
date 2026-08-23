using Microsoft.EntityFrameworkCore;
using LoreAI.Core.Enums;
using LoreAI.Core.Models;
using LoreAI.Infrastructure.Persistence;

namespace LoreAI.Infrastructure.Tests.Persistence;

[Collection("Postgres")]
public class CycleRunRepositoryTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly CycleRunRepository _repository;

    public CycleRunRepositoryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _repository = new CycleRunRepository(fixture.ContextFactory, new PostgresSchemaGuard(fixture.ContextFactory));
    }

    public async ValueTask InitializeAsync()
    {
        await using var context = _fixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"CycleRuns\" RESTART IDENTITY CASCADE");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task GetRecentAsync_NoRuns_ReturnsEmpty()
    {
        var runs = await _repository.GetRecentAsync(3, TestContext.Current.CancellationToken);

        Assert.Empty(runs);
    }

    [Fact]
    public async Task RecordAsync_ThenGetRecentAsync_RoundTripsValues()
    {
        var run = new CycleRun(
            DateTimeOffset.Parse("2026-08-23T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-08-23T12:00:05Z", System.Globalization.CultureInfo.InvariantCulture),
            CycleOutcome.Ok,
            ItemsSeen: 3,
            ItemsProcessed: 3,
            Moved: 2,
            TagsApplied: 5,
            Notified: 1,
            FailureReason: null);

        await _repository.RecordAsync(run, TestContext.Current.CancellationToken);
        var recent = await _repository.GetRecentAsync(3, TestContext.Current.CancellationToken);

        var single = Assert.Single(recent);
        Assert.Equal(run, single);
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsMostRecentCompletedFirst()
    {
        await _repository.RecordAsync(CreateRun(DateTimeOffset.UtcNow.AddMinutes(-30), CycleOutcome.Failed), TestContext.Current.CancellationToken);
        await _repository.RecordAsync(CreateRun(DateTimeOffset.UtcNow.AddMinutes(-15), CycleOutcome.Empty), TestContext.Current.CancellationToken);
        await _repository.RecordAsync(CreateRun(DateTimeOffset.UtcNow, CycleOutcome.Ok), TestContext.Current.CancellationToken);

        var recent = await _repository.GetRecentAsync(2, TestContext.Current.CancellationToken);

        Assert.Equal(2, recent.Count);
        Assert.Equal(CycleOutcome.Ok, recent[0].Outcome);
        Assert.Equal(CycleOutcome.Empty, recent[1].Outcome);
    }

    private static CycleRun CreateRun(DateTimeOffset completedUtc, CycleOutcome outcome) =>
        new(completedUtc.AddSeconds(-5), completedUtc, outcome, 0, 0, 0, 0, 0, null);
}
