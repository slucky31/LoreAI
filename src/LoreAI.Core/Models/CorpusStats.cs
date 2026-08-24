namespace LoreAI.Core.Models;

/// <summary>Vue d'ensemble du corpus indexé (table <c>LibraryItems</c>), exposée par l'outil MCP <c>stats</c> (lot 3).</summary>
public sealed record CorpusStats(
    int TotalItems,
    int ImportantItems,
    int BrokenItems,
    DateTimeOffset? LastIndexedAtUtc);
