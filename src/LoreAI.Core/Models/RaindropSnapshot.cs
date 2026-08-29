namespace LoreAI.Core.Models;

/// <summary>État courant d'un raindrop existant, tel que lu par <c>IRaindropClient.GetRaindropAsync</c> (L3, lot 6).</summary>
/// <param name="Note">Note actuelle côté Raindrop (L5, lot 8) — jamais celle stockée en base, qui date de la classification et peut être obsolète si l'utilisateur l'a modifiée depuis.</param>
public sealed record RaindropSnapshot(long Id, long? CollectionId, IReadOnlyList<string> Tags, bool Broken, string? Note = null);
