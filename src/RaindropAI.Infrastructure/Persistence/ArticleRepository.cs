using System.Text.Json;
using Dapper;
using RaindropAI.Core.Enums;
using RaindropAI.Core.Interfaces;
using RaindropAI.Core.Models;

namespace RaindropAI.Infrastructure.Persistence;

public sealed class ArticleRepository : IArticleRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public ArticleRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task UpsertAsync(RaindropItem item, ClassificationResult classification, DateTimeOffset classifiedAtUtc, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        const string sql = """
            INSERT INTO Articles (
                Id, Title, Link, Excerpt, Note, Tags, CollectionId, Domain, RaindropType,
                RaindropCreatedUtc, RaindropLastUpdateUtc, FetchedAtUtc,
                Category, RecommendedAction, Priority, Reason, ClassificationModel, ClassificationRawResponse, ClassifiedAtUtc
            ) VALUES (
                @Id, @Title, @Link, @Excerpt, @Note, @Tags, @CollectionId, @Domain, @RaindropType,
                @RaindropCreatedUtc, @RaindropLastUpdateUtc, @FetchedAtUtc,
                @Category, @RecommendedAction, @Priority, @Reason, @ClassificationModel, @ClassificationRawResponse, @ClassifiedAtUtc
            )
            ON CONFLICT(Id) DO UPDATE SET
                Title = excluded.Title,
                Link = excluded.Link,
                Excerpt = excluded.Excerpt,
                Note = excluded.Note,
                Tags = excluded.Tags,
                CollectionId = excluded.CollectionId,
                Domain = excluded.Domain,
                RaindropType = excluded.RaindropType,
                RaindropCreatedUtc = excluded.RaindropCreatedUtc,
                RaindropLastUpdateUtc = excluded.RaindropLastUpdateUtc,
                FetchedAtUtc = excluded.FetchedAtUtc,
                Category = excluded.Category,
                RecommendedAction = excluded.RecommendedAction,
                Priority = excluded.Priority,
                Reason = excluded.Reason,
                ClassificationModel = excluded.ClassificationModel,
                ClassificationRawResponse = excluded.ClassificationRawResponse,
                ClassifiedAtUtc = excluded.ClassifiedAtUtc;
            """;

        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            item.Id,
            item.Title,
            item.Link,
            item.Excerpt,
            item.Note,
            Tags = JsonSerializer.Serialize(item.Tags),
            item.CollectionId,
            item.Domain,
            item.RaindropType,
            RaindropCreatedUtc = item.CreatedUtc.UtcDateTime.ToString("O"),
            RaindropLastUpdateUtc = item.LastUpdateUtc?.UtcDateTime.ToString("O"),
            FetchedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            Category = classification.Category.ToString(),
            RecommendedAction = classification.Action.ToString(),
            Priority = classification.Priority.ToString(),
            classification.Reason,
            ClassificationModel = classification.Model,
            ClassificationRawResponse = classification.RawResponse,
            ClassifiedAtUtc = classifiedAtUtc.UtcDateTime.ToString("O"),
        }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<ClassifiedArticle>> GetUnsentDigestItemsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        const string sql = "SELECT * FROM Articles WHERE EmailDigestSentAtUtc IS NULL ORDER BY RaindropCreatedUtc";
        var rows = await connection.QueryAsync<ArticleRow>(new CommandDefinition(sql, cancellationToken: cancellationToken));
        return rows.Select(MapToClassifiedArticle).ToList();
    }

    public async Task MarkDiscordNotifiedAsync(long articleId, DateTimeOffset notifiedAtUtc, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        const string sql = "UPDATE Articles SET DiscordNotifiedAtUtc = @NotifiedAtUtc WHERE Id = @Id";
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = articleId, NotifiedAtUtc = notifiedAtUtc.UtcDateTime.ToString("O") },
            cancellationToken: cancellationToken));
    }

    public async Task MarkDigestSentAsync(IReadOnlyCollection<long> articleIds, DateTimeOffset sentAtUtc, CancellationToken cancellationToken)
    {
        if (articleIds.Count == 0)
        {
            return;
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        const string sql = "UPDATE Articles SET EmailDigestSentAtUtc = @SentAtUtc WHERE Id IN @Ids";
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Ids = articleIds, SentAtUtc = sentAtUtc.UtcDateTime.ToString("O") },
            cancellationToken: cancellationToken));
    }

    private static ClassifiedArticle MapToClassifiedArticle(ArticleRow row)
    {
        var item = new RaindropItem(
            row.Id,
            row.Title,
            row.Link,
            row.Excerpt,
            row.Note,
            string.IsNullOrEmpty(row.Tags) ? [] : JsonSerializer.Deserialize<string[]>(row.Tags) ?? [],
            row.CollectionId,
            row.Domain,
            row.RaindropType,
            DateTimeOffset.Parse(row.RaindropCreatedUtc),
            row.RaindropLastUpdateUtc is null ? null : DateTimeOffset.Parse(row.RaindropLastUpdateUtc));

        var classification = new ClassificationResult(
            Enum.Parse<Category>(row.Category),
            Enum.Parse<RecommendedAction>(row.RecommendedAction),
            Enum.Parse<Priority>(row.Priority),
            row.Reason ?? string.Empty,
            row.ClassificationModel ?? string.Empty,
            row.ClassificationRawResponse ?? string.Empty);

        return new ClassifiedArticle(
            item,
            classification,
            row.ClassifiedAtUtc is null ? DateTimeOffset.UtcNow : DateTimeOffset.Parse(row.ClassifiedAtUtc),
            row.DiscordNotifiedAtUtc is null ? null : DateTimeOffset.Parse(row.DiscordNotifiedAtUtc),
            row.EmailDigestSentAtUtc is null ? null : DateTimeOffset.Parse(row.EmailDigestSentAtUtc));
    }

    private sealed class ArticleRow
    {
        public long Id { get; init; }
        public required string Title { get; init; }
        public required string Link { get; init; }
        public string? Excerpt { get; init; }
        public string? Note { get; init; }
        public string? Tags { get; init; }
        public long? CollectionId { get; init; }
        public string? Domain { get; init; }
        public string? RaindropType { get; init; }
        public required string RaindropCreatedUtc { get; init; }
        public string? RaindropLastUpdateUtc { get; init; }
        public required string FetchedAtUtc { get; init; }
        public required string Category { get; init; }
        public required string RecommendedAction { get; init; }
        public required string Priority { get; init; }
        public string? Reason { get; init; }
        public string? ClassificationModel { get; init; }
        public string? ClassificationRawResponse { get; init; }
        public string? ClassifiedAtUtc { get; init; }
        public string? DiscordNotifiedAtUtc { get; init; }
        public string? EmailDigestSentAtUtc { get; init; }
        public string? WriteBackStatus { get; init; }
    }
}
