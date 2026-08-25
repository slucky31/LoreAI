namespace LoreAI.Core.Enums;

/// <summary>
/// État d'un lien tel que constaté à la dernière passe de <c>ReconciliationJob</c> (L3, lot 6).
/// <c>null</c> côté <see cref="LoreAI.Core.Models.TrackedArticle"/>/<c>ArticleEntity</c> signifie
/// « jamais réconcilié », distinct d'<see cref="Ok"/> qui est une constatation positive.
/// </summary>
public enum LinkStatus
{
    Ok,
    Broken,

    /// <summary>L'item n'existe plus côté Raindrop (404) — supprimé définitivement, pas seulement mis à la corbeille.</summary>
    Deleted,
}
