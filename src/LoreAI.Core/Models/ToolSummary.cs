namespace LoreAI.Core.Models;

/// <summary>Ligne de la base d'outils (S7, lot 5), projection allégée pour le catalogue MCP.</summary>
public sealed record ToolSummary(
    long Id,
    string Name,
    string? Category,
    string Status,
    string? Verdict,
    int RelatedArticleCount,
    DateTimeOffset FirstSeenAtUtc,
    DateTimeOffset LastSeenAtUtc);
