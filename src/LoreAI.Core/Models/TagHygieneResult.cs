namespace LoreAI.Core.Models;

/// <summary>Deux tags ou plus dont l'orthographe se ressemble au point d'être probablement le même concept (N2).</summary>
public sealed record TagCluster(IReadOnlyList<string> Tags);

/// <summary>
/// Résultat de <c>TagHygieneAnalyzer</c> (N2) : grappes de tags proches, et tags utilisés une seule fois —
/// les deux volets explicitement demandés par la roadmap pour ce scénario. Rapport seul, jamais d'action
/// automatique (fusion ou suppression de tag).
/// </summary>
public sealed record TagHygieneResult(IReadOnlyList<TagCluster> Clusters, IReadOnlyList<string> SingleUseTags);
