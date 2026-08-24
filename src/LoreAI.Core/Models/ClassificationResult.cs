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

    public static ClassificationResult Fallback(string model, string reason, string rawResponse) =>
        new(null, [], RecommendedAction.Reference, Priority.Basse, reason, string.Empty, model, rawResponse) { IsFallback = true };
}
