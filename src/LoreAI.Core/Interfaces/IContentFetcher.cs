using LoreAI.Core.Models;

namespace LoreAI.Core.Interfaces;

/// <summary>Récupération best-effort du contenu réel d'un article (S1, lot 4) — ne lève jamais pour un échec attendu.</summary>
public interface IContentFetcher
{
    Task<ContentFetchResult> FetchAsync(string url, CancellationToken cancellationToken);
}
