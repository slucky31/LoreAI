using Microsoft.EntityFrameworkCore;
using LoreAI.Infrastructure.Persistence;

namespace LoreAI.Infrastructure.Tests.Persistence;

[Collection("Postgres")]
public class ToolRepositoryTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly ToolRepository _repository;

    public ToolRepositoryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _repository = new ToolRepository(fixture.ContextFactory, new PostgresSchemaGuard(fixture.ContextFactory));
    }

    public async ValueTask InitializeAsync()
    {
        await using var context = _fixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Tools\" RESTART IDENTITY CASCADE");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task UpsertFromArticleAsync_NewTool_CreatesRowWithDefaultStatus()
    {
        var seenAt = DateTimeOffset.UtcNow;

        await _repository.UpsertFromArticleAsync("Ollama", "CLI", 1, seenAt, TestContext.Current.CancellationToken);

        await using var context = _fixture.CreateContext();
        var tool = await context.Tools.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Ollama", tool.Name);
        Assert.Equal("CLI", tool.Category);
        Assert.Equal("À évaluer", tool.Status);
        Assert.Null(tool.Verdict);
        Assert.Equal([1], tool.RelatedArticleIds);
    }

    [Fact]
    public async Task UpsertFromArticleAsync_ExistingToolCaseInsensitiveMatch_AddsRelatedArticleWithoutDuplicating()
    {
        await _repository.UpsertFromArticleAsync("Ollama", "CLI", 1, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        await _repository.UpsertFromArticleAsync("ollama", "CLI", 2, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        await using var context = _fixture.CreateContext();
        var tool = await context.Tools.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal([1, 2], tool.RelatedArticleIds);
    }

    [Fact]
    public async Task UpsertFromArticleAsync_SameArticleTwice_DoesNotDuplicateId()
    {
        await _repository.UpsertFromArticleAsync("Ollama", "CLI", 1, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        await _repository.UpsertFromArticleAsync("Ollama", "CLI", 1, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        await using var context = _fixture.CreateContext();
        var tool = await context.Tools.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal([1], tool.RelatedArticleIds);
    }

    [Fact]
    public async Task UpsertFromArticleAsync_ExistingTool_UpdatesLastSeenButNeverStatusOrVerdict()
    {
        await using (var context = _fixture.CreateContext())
        {
            context.Tools.Add(new ToolEntity
            {
                Name = "Ollama",
                Category = "CLI",
                Status = "Retenu",
                Verdict = "Excellent",
                RelatedArticleIds = [1],
                FirstSeenAtUtc = DateTimeOffset.UnixEpoch,
                LastSeenAtUtc = DateTimeOffset.UnixEpoch,
            });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var newSeenAt = DateTimeOffset.UtcNow;
        await _repository.UpsertFromArticleAsync("Ollama", "CLI", 2, newSeenAt, TestContext.Current.CancellationToken);

        await using var readContext = _fixture.CreateContext();
        var tool = await readContext.Tools.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Retenu", tool.Status);
        Assert.Equal("Excellent", tool.Verdict);
        Assert.Equal(newSeenAt, tool.LastSeenAtUtc, TimeSpan.FromMilliseconds(1));
    }
}
