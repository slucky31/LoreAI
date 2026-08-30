using Microsoft.EntityFrameworkCore;
using LoreAI.Core.Models;
using LoreAI.Infrastructure.Persistence;

namespace LoreAI.Infrastructure.Tests.Persistence;

[Collection("Postgres")]
public class WatchTopicRepositoryTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly WatchTopicRepository _repository;

    public WatchTopicRepositoryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _repository = new WatchTopicRepository(fixture.ContextFactory, new PostgresSchemaGuard(fixture.ContextFactory));
    }

    public async ValueTask InitializeAsync()
    {
        await using var context = _fixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"WatchTopics\" RESTART IDENTITY CASCADE");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task GetAllAsync_NoTopics_ReturnsEmpty()
    {
        var topics = await _repository.GetAllAsync(TestContext.Current.CancellationToken);

        Assert.Empty(topics);
    }

    [Fact]
    public async Task AddAsync_ThenGetAllAsync_RoundTripsValuesWithGeneratedId()
    {
        var topic = new WatchTopic(0, "dotnet-perf", "Optimisations .NET", 7, 42, "0", DateTimeOffset.UtcNow);

        var id = await _repository.AddAsync(topic, TestContext.Current.CancellationToken);
        var all = await _repository.GetAllAsync(TestContext.Current.CancellationToken);

        var stored = Assert.Single(all);
        Assert.Equal(id, stored.Id);
        Assert.NotEqual(0, id);
        Assert.Equal("dotnet-perf", stored.Name);
        Assert.Equal("Optimisations .NET", stored.Description);
        Assert.Equal(7, stored.MinifluxCategoryId);
        Assert.Equal(42, stored.RaindropCollectionId);
        Assert.Equal("0", stored.LastMinifluxEntryId);
    }

    [Fact]
    public async Task UpdateCursorAsync_UpdatesOnlyTheTargetedTopic()
    {
        var topicA = new WatchTopic(0, "sujet-a", "desc", 1, 10, "0", DateTimeOffset.UtcNow);
        var topicB = new WatchTopic(0, "sujet-b", "desc", 2, 20, "0", DateTimeOffset.UtcNow);
        var idA = await _repository.AddAsync(topicA, TestContext.Current.CancellationToken);
        var idB = await _repository.AddAsync(topicB, TestContext.Current.CancellationToken);

        await _repository.UpdateCursorAsync(idA, "123", TestContext.Current.CancellationToken);

        var all = await _repository.GetAllAsync(TestContext.Current.CancellationToken);
        Assert.Equal("123", all.Single(t => t.Id == idA).LastMinifluxEntryId);
        Assert.Equal("0", all.Single(t => t.Id == idB).LastMinifluxEntryId);
    }

    [Fact]
    public async Task UpdateCursorAsync_UnknownTopicId_DoesNotThrow()
    {
        var exception = await Record.ExceptionAsync(() => _repository.UpdateCursorAsync(999, "1", TestContext.Current.CancellationToken));

        Assert.Null(exception);
    }
}
