namespace LoreAI.Core.Interfaces;

/// <summary>
/// Écriture de la base d'outils (S7, lot 5) — Worker uniquement, appelée par <c>UnsortedClassificationJob</c>
/// quand <c>Action == ATester</c>. Rapprochement par nom insensible à la casse : ajoute
/// <paramref name="articleId"/> aux articles liés d'un outil déjà connu (sans jamais toucher son
/// statut/verdict, manuels) ou crée une nouvelle ligne.
/// </summary>
public interface IToolRepository
{
    Task UpsertFromArticleAsync(string name, string? category, long articleId, DateTimeOffset seenAtUtc, CancellationToken cancellationToken);
}
