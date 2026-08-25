using LoreAI.Core.Enums;

namespace LoreAI.Core.Models;

/// <summary>Article suivi (jamais un repli) dont le lien est cassé ou supprimé (N3, lot 6).</summary>
public sealed record BrokenTrackedArticle(long Id, string Title, string Url, LinkStatus LinkStatus);
