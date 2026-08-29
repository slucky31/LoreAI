using LoreAI.Core.Enums;

namespace LoreAI.Infrastructure.Persistence;

/// <summary>
/// Forme persistée d'un article classifié. Distincte des records immuables de <c>Core</c> : c'est la
/// classe qu'EF Core suit et matérialise, mappée vers/depuis <c>Item</c>/<c>ClassificationResult</c>
/// dans <see cref="ArticleRepository"/> — même séparation que l'ancien <c>ArticleRow</c> de Dapper.
/// </summary>
public sealed class ArticleEntity
{
    // Généré par la base (lot 8, #49) : un lien Newsletter n'a pas d'id Raindrop numérique à réutiliser
    // tel quel. La clé applicative est désormais (SourceType, SourceId) ; Id reste l'identifiant technique
    // exposé aux autres repositories (write-back, réconciliation, base d'outils...).
    public long Id { get; set; }
    public SourceType SourceType { get; set; }
    public required string SourceId { get; set; }
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

    // Un résultat de repli (ClassificationResult.IsFallback) est bien persisté (UpsertAsync a lieu avant
    // le test IsFallback dans UnsortedClassificationJob.Invoke), mais ne représente pas une vraie décision
    // du modèle. Ce booléen le distingue proprement en base — S4 (lot 5) l'exclut des revues mensuelles.
    public bool IsFallback { get; set; }

    public bool Moved { get; set; }
    public string? WriteBackStatus { get; set; }
    public DateTimeOffset? WriteBackAtUtc { get; set; }
    public DateTimeOffset? DiscordNotifiedAtUtc { get; set; }

    // Id de collection réellement écrit au write-back (null = resté en Non trié) — la référence que
    // ReconciliationJob (L3, lot 6) compare à l'état réel pour détecter un déplacement humain.
    public long? WriteBackCollectionId { get; set; }

    // L3 (lot 6) : null tant qu'aucune passe de réconciliation n'a eu lieu, distinct de LinkStatus.Ok
    // qui est une constatation positive. HumanHandledAtUtc n'est jamais réinitialisé une fois posé.
    public DateTimeOffset? LastSeenAtUtc { get; set; }
    public DateTimeOffset? HumanHandledAtUtc { get; set; }
    public LinkStatus? LinkStatus { get; set; }

    // L4 (lot 6) : relance envoyée une seule fois.
    public DateTimeOffset? RemindedAtUtc { get; set; }

    // L5 (lot 8) : null tant que l'article n'est pas dans la file de lecture de la semaine — sert à
    // savoir quoi détaguer au prochain passage de ReadingQueueTaggingJob, jamais réinitialisé à la main.
    public DateTimeOffset? ReadingQueueTaggedAtUtc { get; set; }

    // Contenu réel (S1, lot 4) : toujours nullable, alimenté au mieux (best-effort) — un article
    // pré-lot-4 ou dont le fetch a échoué n'a simplement jamais ces champs renseignés.
    public string? ContentText { get; set; }
    public DateTimeOffset? ContentFetchedAtUtc { get; set; }
    public ContentFetchStatus? ContentStatus { get; set; }
    public int? WordCount { get; set; }
    public string? Summary { get; set; }

    // S7 (lot 5) / S9 (lot 6) : uniquement renseignés quand RecommendedAction == ATester, cf. ClassificationResult.
    public string? ToolName { get; set; }
    public string? ToolCategory { get; set; }
    public string? ToolUrl { get; set; }
}
