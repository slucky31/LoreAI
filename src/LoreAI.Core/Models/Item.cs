using LoreAI.Core.Enums;

namespace LoreAI.Core.Models;

/// <summary>
/// Modèle central du pipeline, indépendant de la source (ADR 0012). Clé naturelle <c>(SourceType, SourceId)</c>.
/// <c>Note</c>/<c>Tags</c> restent portés ici même si toutes les sources ne les renseignent pas : Raindrop
/// en a besoin pour la fusion au write-back et pour le contexte du prompt de classification, et une source
/// future sans ces notions se contente de <c>Tags = []</c>/<c>Note = null</c>.
/// </summary>
public sealed record Item(
    SourceType SourceType,
    string SourceId,
    string Url,
    string Title,
    string? Excerpt,
    string? Note,
    IReadOnlyList<string> Tags,
    DateTimeOffset CapturedAtUtc);
