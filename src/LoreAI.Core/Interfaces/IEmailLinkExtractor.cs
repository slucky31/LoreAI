using LoreAI.Core.Models;

namespace LoreAI.Core.Interfaces;

/// <summary>
/// Tranche, parmi les URLs candidates d'un mail (déjà réduites par le filtre heuristique gratuit), lesquelles
/// sont de vrais articles — symétrique à <see cref="IClassifier"/> mais en amont (lot 8, #49). Une liste vide
/// est un résultat légitime (même philosophie que <c>ClassificationResult.Fallback</c>), jamais une erreur.
/// </summary>
public interface IEmailLinkExtractor
{
    Task<IReadOnlyList<ExtractedLink>> ExtractAsync(string subject, string body, IReadOnlyList<string> candidateUrls, CancellationToken cancellationToken);
}
