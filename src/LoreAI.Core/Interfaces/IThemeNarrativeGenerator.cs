using LoreAI.Core.Models;

namespace LoreAI.Core.Interfaces;

/// <summary>
/// Génère la narration d'un thème pour la revue mensuelle (S4, lot 5) — un appel LLM par thème, texte
/// libre (pas de tool-use, contrairement à <see cref="IClassifier"/>).
/// </summary>
public interface IThemeNarrativeGenerator
{
    Task<string> GenerateNarrativeAsync(string theme, IReadOnlyList<MonthlyReviewArticle> articles, CancellationToken cancellationToken);
}
