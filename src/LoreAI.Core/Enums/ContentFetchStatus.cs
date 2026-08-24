namespace LoreAI.Core.Enums;

/// <summary>
/// Résultat best-effort de la récupération du contenu réel d'un article (S1, lot 4). Un statut différent
/// de <see cref="Success"/> ne bloque jamais le pipeline : la classification retombe sur l'excerpt Raindrop.
/// </summary>
public enum ContentFetchStatus
{
    Success,
    HttpError,
    Timeout,
    UnsupportedContentType,

    /// <summary>Texte extrait trop court pour être exploitable — proxy heuristique pour une page paywall/JS-only.</summary>
    ExtractionEmpty,
    Error,

    /// <summary>Fetch désactivé par <c>Worker__FetchArticleContent=false</c> : aucune tentative n'a eu lieu.</summary>
    Skipped,
}
