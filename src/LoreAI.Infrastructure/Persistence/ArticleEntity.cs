using LoreAI.Core.Enums;

namespace LoreAI.Infrastructure.Persistence;

/// <summary>
/// Forme persistée d'un article classifié. Distincte des records immuables de <c>Core</c> : c'est la
/// classe qu'EF Core suit et matérialise, mappée vers/depuis <c>RaindropItem</c>/<c>ClassificationResult</c>
/// dans <see cref="ArticleRepository"/> — même séparation que l'ancien <c>ArticleRow</c> de Dapper.
/// </summary>
public sealed class ArticleEntity
{
    public long Id { get; set; }
    public required string Title { get; set; }
    public required string Link { get; set; }
    public string? Excerpt { get; set; }
    public string? Note { get; set; }
    public string[] OriginalTags { get; set; } = [];
    public long? CollectionId { get; set; }
    public string? Domain { get; set; }
    public string? RaindropType { get; set; }
    public DateTimeOffset RaindropCreatedUtc { get; set; }
    public DateTimeOffset? RaindropLastUpdateUtc { get; set; }
    public DateTimeOffset FetchedAtUtc { get; set; }

    public string? SuggestedCollection { get; set; }
    public string[] SuggestedTags { get; set; } = [];
    public RecommendedAction RecommendedAction { get; set; }
    public Priority Priority { get; set; }
    public string? Reason { get; set; }
    public string? ClassificationModel { get; set; }
    public string? ClassificationRawResponse { get; set; }
    public DateTimeOffset? ClassifiedAtUtc { get; set; }

    public bool Moved { get; set; }
    public string? WriteBackStatus { get; set; }
    public DateTimeOffset? WriteBackAtUtc { get; set; }
    public DateTimeOffset? DiscordNotifiedAtUtc { get; set; }
    public DateTimeOffset? EmailDigestSentAtUtc { get; set; }
}
