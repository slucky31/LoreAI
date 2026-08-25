namespace LoreAI.Core.Models;

/// <summary>Article « À lire » jamais traité depuis longtemps (N4, lot 6) — proposition de purge, jamais une suppression.</summary>
public sealed record StaleArticle(long Id, string Title, string Url, int DaysSinceCaptured);
