namespace LoreAI.Core.Models;

/// <summary>
/// Jugement du LLM sur une entrée candidate de veille (C4, lot 9, #50), pour un sujet déjà connu (ingestion
/// par catégorie Miniflux dédiée — pas de désambiguïsation entre plusieurs sujets ici).
/// </summary>
public sealed record WatchEvaluation(
    bool IsRelevant,
    bool IsNew,
    IReadOnlyList<string> Tags,
    string Reason,
    string Model,
    string RawResponse)
{
    /// <summary>
    /// Vrai quand l'évaluation a échoué (transport, parsing) : les valeurs portées ici sont des valeurs de
    /// repli, jamais une décision du modèle. Un repli ne doit jamais déclencher de création Raindrop — même
    /// philosophie que <see cref="ClassificationResult.Fallback"/>.
    /// </summary>
    public bool IsFallback { get; init; }

    public static WatchEvaluation Fallback(string model, string reason, string rawResponse) =>
        new(false, false, [], reason, model, rawResponse) { IsFallback = true };
}
