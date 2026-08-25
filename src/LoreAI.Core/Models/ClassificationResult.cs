using LoreAI.Core.Enums;

namespace LoreAI.Core.Models;

public sealed record ClassificationResult(
    string? SuggestedCollection,
    IReadOnlyList<string> Tags,
    RecommendedAction Action,
    Priority Priority,
    string Reason,
    string Summary,
    string Model,
    string RawResponse)
{
    /// <summary>
    /// Vrai quand la classification a échoué : les valeurs portées ici sont des valeurs de repli,
    /// pas une décision du modèle. Un résultat de repli ne doit jamais être appliqué à Raindrop —
    /// il écrirait une trace d'erreur technique dans les données réelles de l'utilisateur.
    /// </summary>
    public bool IsFallback { get; init; }

    /// <summary>Nom court de l'outil/produit (S7, lot 5) — renseigné par le modèle uniquement quand <see cref="Action"/> vaut <see cref="RecommendedAction.ATester"/>, <c>null</c> sinon.</summary>
    public string? ToolName { get; init; }

    /// <summary>Catégorie libre de l'outil (S7, lot 5), même condition que <see cref="ToolName"/>.</summary>
    public string? ToolCategory { get; init; }

    /// <summary>Lien vers le dépôt/site officiel de l'outil (S9, lot 6), même condition que <see cref="ToolName"/>.</summary>
    public string? ToolUrl { get; init; }

    public static ClassificationResult Fallback(string model, string reason, string rawResponse) =>
        new(null, [], RecommendedAction.Reference, Priority.Basse, reason, string.Empty, model, rawResponse) { IsFallback = true };
}
