using LoreAI.Core.Enums;

namespace LoreAI.Infrastructure.Persistence;

/// <summary>
/// Forme persistée d'un article classifié. Distincte des records immuables de <c>Core</c> : c'est la
/// classe qu'EF Core suit et matérialise, mappée vers/depuis <c>Item</c>/<c>ClassificationResult</c>
/// dans <see cref="ArticleRepository"/> — même séparation que l'ancien <c>ArticleRow</c> de Dapper.
/// L'identifiant reste l'id Raindrop numérique (ADR 0012) : généraliser la clé en <c>(SourceType, SourceId)</c>
/// n'a de sens que lorsqu'une deuxième source existe réellement.
/// </summary>
public sealed class ArticleEntity
{
    public long Id { get; set; }
    public required string Title { get; set; }
    public required string Url { get; set; }
    public string? Excerpt { get; set; }
    public string? Note { get; set; }
    public string[] OriginalTags { get; set; } = [];
    public DateTimeOffset CapturedAtUtc { get; set; }
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

    // Contenu réel (S1, lot 4) : toujours nullable, alimenté au mieux (best-effort) — un article
    // pré-lot-4 ou dont le fetch a échoué n'a simplement jamais ces champs renseignés.
    public string? ContentText { get; set; }
    public DateTimeOffset? ContentFetchedAtUtc { get; set; }
    public ContentFetchStatus? ContentStatus { get; set; }
    public int? WordCount { get; set; }
    public string? Summary { get; set; }
}
