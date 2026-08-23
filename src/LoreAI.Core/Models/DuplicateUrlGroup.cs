namespace LoreAI.Core.Models;

/// <summary>Un item impliqué dans un doublon d'URL — juste assez pour l'afficher dans le rapport (N1).</summary>
public sealed record DuplicateLink(long Id, string Title, string Url);

/// <summary>Deux items ou plus dont l'URL normalisée coïncide (N1) — jamais fusionnés automatiquement.</summary>
public sealed record DuplicateUrlGroup(string NormalizedUrl, IReadOnlyList<DuplicateLink> Items);
