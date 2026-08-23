namespace LoreAI.Core.Models;

/// <summary>Une collection Raindrop à 1 ou 2 items (N5) — signal faible qu'elle mériterait d'être fusionnée ou supprimée.</summary>
public sealed record UnbalancedCollection(string Title, int ItemCount);
