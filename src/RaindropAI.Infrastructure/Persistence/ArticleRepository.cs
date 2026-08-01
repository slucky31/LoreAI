using System.Globalization;
using System.Text.Json;
using Dapper;
using RaindropAI.Core.Enums;
using RaindropAI.Core.Interfaces;
using RaindropAI.Core.Models;

namespace RaindropAI.Infrastructure.Persistence;

public sealed class ArticleRepository : IArticleRepository
{
    /// <summary>Taille des lots pour les clauses <c>IN</c>, bien en deçà de la limite de variables de SQLite.</summary>
    private const int BatchSize = 500;

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
                Id, Title, Link, Excerpt, Note, OriginalTags, CollectionId, Domain, RaindropType,
                RaindropCreatedUtc, RaindropLastUpdateUtc, FetchedAtUtc,
                SuggestedCollection, SuggestedTags, RecommendedAction, Priority, Reason,
                ClassificationModel, ClassificationRawResponse, ClassifiedAtUtc
            ) VALUES (
                @Id, @Title, @Link, @Excerpt, @Note, @OriginalTags, @CollectionId, @Domain, @RaindropType,
                @RaindropCreatedUtc, @RaindropLastUpdateUtc, @FetchedAtUtc,
                @SuggestedCollection, @SuggestedTags, @RecommendedAction, @Priority, @Reason,
                @ClassificationModel, @ClassificationRawResponse, @ClassifiedAtUtc
            )
            ON CONFLICT(Id) DO UPDATE SET
                Title = excluded.Title,
                Link = excluded.Link,
                Excerpt = excluded.Excerpt,
                Note = excluded.Note,
                OriginalTags = excluded.OriginalTags,
                CollectionId = excluded.CollectionId,
                Domain = excluded.Domain,
                RaindropType = excluded.RaindropType,
                RaindropCreatedUtc = excluded.RaindropCreatedUtc,
                RaindropLastUpdateUtc = excluded.RaindropLastUpdateUtc,
                FetchedAtUtc = excluded.FetchedAtUtc,
                SuggestedCollection = excluded.SuggestedCollection,
                SuggestedTags = excluded.SuggestedTags,
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
            OriginalTags = JsonSerializer.Serialize(item.Tags),
            item.CollectionId,
            item.Domain,
            item.RaindropType,
            RaindropCreatedUtc = item.CreatedUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            RaindropLastUpdateUtc = item.LastUpdateUtc?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            FetchedAtUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            classification.SuggestedCollection,
            SuggestedTags = JsonSerializer.Serialize(classification.Tags),
            RecommendedAction = classification.Action.ToString(),
            Priority = classification.Priority.ToString(),
            classification.Reason,
            ClassificationModel = classification.Model,
            ClassificationRawResponse = classification.RawResponse,
            ClassifiedAtUtc = classifiedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        }, cancellationToken: cancellationToken));
    }

    public async Task RecordWriteBackAsync(long articleId, bool success, bool moved, DateTimeOffset atUtc, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        const string sql = "UPDATE Articles SET WriteBackStatus = @Status, Moved = @Moved, WriteBackAtUtc = @AtUtc WHERE Id = @Id";
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                Id = articleId,
                Status = success ? "Done" : "Failed",
                Moved = moved ? 1 : 0,
                AtUtc = atUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<ClassifiedArticle>> GetUnsentDigestItemsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        // Colonnes listées explicitement : un `SELECT *` obligerait ArticleRow à suivre le schéma à la
        // trace, et masquerait l'oubli d'un champ derrière une valeur par défaut silencieuse.
        const string sql = """
            SELECT
                Id, Title, Link, Excerpt, Note, OriginalTags, CollectionId, Domain, RaindropType,
                RaindropCreatedUtc, RaindropLastUpdateUtc, FetchedAtUtc,
                SuggestedCollection, SuggestedTags, RecommendedAction, Priority, Reason,
                ClassificationModel, ClassificationRawResponse, ClassifiedAtUtc,
                Moved, WriteBackStatus, DiscordNotifiedAtUtc, EmailDigestSentAtUtc
            FROM Articles
            WHERE EmailDigestSentAtUtc IS NULL
            ORDER BY RaindropCreatedUtc
            """;
        var rows = await connection.QueryAsync<ArticleRow>(new CommandDefinition(sql, cancellationToken: cancellationToken));
        return rows.Select(MapToClassifiedArticle).ToList();
    }

    public async Task MarkDiscordNotifiedAsync(long articleId, DateTimeOffset notifiedAtUtc, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        const string sql = "UPDATE Articles SET DiscordNotifiedAtUtc = @NotifiedAtUtc WHERE Id = @Id";
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = articleId, NotifiedAtUtc = notifiedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) },
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

        // Dapper développe la clause IN en un paramètre par identifiant : sur un digest volumineux
        // (premier backfill), on dépasserait la limite de variables de SQLite. D'où le découpage.
        foreach (var batch in articleIds.Chunk(BatchSize))
        {
            await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new { Ids = batch, SentAtUtc = sentAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) },
                cancellationToken: cancellationToken));
        }
    }

    private static ClassifiedArticle MapToClassifiedArticle(ArticleRow row)
    {
        var item = new RaindropItem(
            row.Id,
            row.Title,
            row.Link,
            row.Excerpt,
            row.Note,
            string.IsNullOrEmpty(row.OriginalTags) ? [] : JsonSerializer.Deserialize<string[]>(row.OriginalTags) ?? [],
            row.CollectionId,
            row.Domain,
            row.RaindropType,
            DateTimeOffset.Parse(row.RaindropCreatedUtc, CultureInfo.InvariantCulture),
            row.RaindropLastUpdateUtc is null ? null : DateTimeOffset.Parse(row.RaindropLastUpdateUtc, CultureInfo.InvariantCulture));

        var classification = new ClassificationResult(
            row.SuggestedCollection,
            string.IsNullOrEmpty(row.SuggestedTags) ? [] : JsonSerializer.Deserialize<string[]>(row.SuggestedTags) ?? [],
            Enum.Parse<RecommendedAction>(row.RecommendedAction),
            Enum.Parse<Priority>(row.Priority),
            row.Reason ?? string.Empty,
            row.ClassificationModel ?? string.Empty,
            row.ClassificationRawResponse ?? string.Empty);

        return new ClassifiedArticle(
            item,
            classification,
            row.ClassifiedAtUtc is null ? DateTimeOffset.UtcNow : DateTimeOffset.Parse(row.ClassifiedAtUtc, CultureInfo.InvariantCulture),
            row.Moved != 0,
            row.DiscordNotifiedAtUtc is null ? null : DateTimeOffset.Parse(row.DiscordNotifiedAtUtc, CultureInfo.InvariantCulture),
            row.EmailDigestSentAtUtc is null ? null : DateTimeOffset.Parse(row.EmailDigestSentAtUtc, CultureInfo.InvariantCulture));
    }

    private sealed class ArticleRow
    {
        public long Id { get; init; }
        public required string Title { get; init; }
        public required string Link { get; init; }
        public string? Excerpt { get; init; }
        public string? Note { get; init; }
        public string? OriginalTags { get; init; }
        public long? CollectionId { get; init; }
        public string? Domain { get; init; }
        public string? RaindropType { get; init; }
        public required string RaindropCreatedUtc { get; init; }
        public string? RaindropLastUpdateUtc { get; init; }
        public required string FetchedAtUtc { get; init; }
        public string? SuggestedCollection { get; init; }
        public string? SuggestedTags { get; init; }
        public required string RecommendedAction { get; init; }
        public required string Priority { get; init; }
        public string? Reason { get; init; }
        public string? ClassificationModel { get; init; }
        public string? ClassificationRawResponse { get; init; }
        public string? ClassifiedAtUtc { get; init; }
        public int Moved { get; init; }
        public string? DiscordNotifiedAtUtc { get; init; }
        public string? EmailDigestSentAtUtc { get; init; }
        public string? WriteBackStatus { get; init; }
    }
}
