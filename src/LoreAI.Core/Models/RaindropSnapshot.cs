namespace LoreAI.Core.Models;

/// <summary>État courant d'un raindrop existant, tel que lu par <c>IRaindropClient.GetRaindropAsync</c> (L3, lot 6).</summary>
public sealed record RaindropSnapshot(long Id, long? CollectionId, IReadOnlyList<string> Tags, bool Broken);
