namespace LoreAI.Core.Models;

/// <summary>Collections et tags réellement en place dans le compte Raindrop de l'utilisateur, appris à chaque cycle.</summary>
public sealed record RaindropTaxonomy(
    IReadOnlyList<RaindropCollection> Collections,
    IReadOnlyList<RaindropTag> Tags);
