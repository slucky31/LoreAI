using Microsoft.EntityFrameworkCore;
using LoreAI.Infrastructure.Persistence;

namespace LoreAI.Infrastructure.Tests.Persistence;

[Collection("Postgres")]
public class CorpusQueryRepositoryTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly CorpusQueryRepository _repository;

    public CorpusQueryRepositoryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _repository = new CorpusQueryRepository(fixture.ContextFactory);
    }

    public async ValueTask InitializeAsync()
    {
        await using var context = _fixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"LibraryItems\" RESTART IDENTITY CASCADE");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task GetByIdAsync_ExistingId_ReturnsSummary()
    {
        await SeedAsync(CreateItem(1, "Titre", "https://example.com/a", tags: ["dotnet"]));

        var summary = await _repository.GetByIdAsync(1, TestContext.Current.CancellationToken);

        Assert.NotNull(summary);
        Assert.Equal(1, summary.Id);
        Assert.Equal("Titre", summary.Title);
        Assert.Equal(["dotnet"], summary.Tags);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        var summary = await _repository.GetByIdAsync(999, TestContext.Current.CancellationToken);

        Assert.Null(summary);
    }

    [Fact]
    public async Task GetRecentAsync_OrdersByCapturedAtDescending()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(
            CreateItem(1, "Le plus ancien", "https://example.com/1", capturedAtUtc: now.AddDays(-2)),
            CreateItem(2, "Le plus récent", "https://example.com/2", capturedAtUtc: now),
            CreateItem(3, "Au milieu", "https://example.com/3", capturedAtUtc: now.AddDays(-1)));

        var recent = await _repository.GetRecentAsync(10, TestContext.Current.CancellationToken);

        Assert.Equal([2, 3, 1], recent.Select(i => i.Id));
    }

    [Fact]
    public async Task GetRecentAsync_RespectsCount()
    {
        await SeedAsync(CreateItem(1, "A", "https://example.com/1"), CreateItem(2, "B", "https://example.com/2"));

        var recent = await _repository.GetRecentAsync(1, TestContext.Current.CancellationToken);

        Assert.Single(recent);
    }

    [Fact]
    public async Task SearchAsync_MatchesTitleCaseInsensitively()
    {
        await SeedAsync(CreateItem(1, "Introduction à DOTNET", "https://example.com/a"));

        var results = await _repository.SearchAsync("dotnet", 10, TestContext.Current.CancellationToken);

        Assert.Single(results);
    }

    [Fact]
    public async Task SearchAsync_MatchesUrlWhenTitleDoesNotMatch()
    {
        await SeedAsync(CreateItem(1, "Un article", "https://example.com/raindrop-tips"));

        var results = await _repository.SearchAsync("raindrop", 10, TestContext.Current.CancellationToken);

        Assert.Single(results);
    }

    [Fact]
    public async Task SearchAsync_NoMatch_ReturnsEmpty()
    {
        await SeedAsync(CreateItem(1, "Un article", "https://example.com/a"));

        var results = await _repository.SearchAsync("absent", 10, TestContext.Current.CancellationToken);

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_RespectsLimit()
    {
        await SeedAsync(
            CreateItem(1, "dotnet 1", "https://example.com/1"),
            CreateItem(2, "dotnet 2", "https://example.com/2"),
            CreateItem(3, "dotnet 3", "https://example.com/3"));

        var results = await _repository.SearchAsync("dotnet", 2, TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task GetStatsAsync_CountsTotalImportantAndBroken()
    {
        await SeedAsync(
            CreateItem(1, "A", "https://example.com/1", important: true),
            CreateItem(2, "B", "https://example.com/2", broken: true),
            CreateItem(3, "C", "https://example.com/3"));

        var stats = await _repository.GetStatsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, stats.TotalItems);
        Assert.Equal(1, stats.ImportantItems);
        Assert.Equal(1, stats.BrokenItems);
    }

    [Fact]
    public async Task GetStatsAsync_ReturnsMostRecentIndexedAtUtc()
    {
        var older = DateTimeOffset.UtcNow.AddDays(-1);
        var newer = DateTimeOffset.UtcNow;
        await SeedAsync(
            CreateItem(1, "A", "https://example.com/1", indexedAtUtc: older),
            CreateItem(2, "B", "https://example.com/2", indexedAtUtc: newer));

        var stats = await _repository.GetStatsAsync(TestContext.Current.CancellationToken);

        // Postgres timestamptz stocke en microsecondes : une comparaison stricte casserait sur l'arrondi
        // de la dernière décimale (ticks .NET en centaines de nanosecondes).
        Assert.NotNull(stats.LastIndexedAtUtc);
        Assert.Equal(newer, stats.LastIndexedAtUtc.Value, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task GetStatsAsync_EmptyCorpus_ReturnsZeroesAndNullLastIndexedAtUtc()
    {
        var stats = await _repository.GetStatsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, stats.TotalItems);
        Assert.Null(stats.LastIndexedAtUtc);
    }

    private async Task SeedAsync(params LibraryItemEntity[] items)
    {
        await using var context = _fixture.CreateContext();
        context.LibraryItems.AddRange(items);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static LibraryItemEntity CreateItem(
        long id,
        string title,
        string url,
        string[]? tags = null,
        DateTimeOffset? capturedAtUtc = null,
        DateTimeOffset? indexedAtUtc = null,
        bool important = false,
        bool broken = false) => new()
        {
            Id = id,
            SourceType = "Raindrop",
            Title = title,
            Url = url,
            Tags = tags ?? [],
            CapturedAtUtc = capturedAtUtc ?? DateTimeOffset.UtcNow,
            Origin = "Library",
            Important = important,
            Broken = broken,
            IndexedAtUtc = indexedAtUtc ?? DateTimeOffset.UtcNow,
        };
}
