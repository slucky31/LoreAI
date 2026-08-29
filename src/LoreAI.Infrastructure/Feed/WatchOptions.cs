using System.ComponentModel.DataAnnotations;
using LoreAI.Core.Interfaces;

namespace LoreAI.Infrastructure.Feed;

/// <summary>Sujets de veille (C4, lot 9, #50) et catégorie Miniflux dédiée qui porte les flux RSS de recherche.</summary>
public sealed class WatchOptions
{
    /// <summary>Id de la catégorie Miniflux (créée manuellement dans l'UI, ex. « Veille ») qui contient les flux de recherche — jamais les flux de lecture personnelle du lot 7.</summary>
    [Range(1, int.MaxValue)]
    public int MinifluxCategoryId { get; init; }

    /// <summary>Sujets suivis, servant de contexte au LLM (<see cref="ITopicWatchFilter"/>) — pas de mapping flux→sujet, un seul filtrage juge chaque entrée contre tous les sujets.</summary>
    public IReadOnlyList<WatchTopicOptions> Topics { get; init; } = [];
}

public sealed class WatchTopicOptions
{
    [Required(AllowEmptyStrings = false)]
    public required string Name { get; init; }

    [Required(AllowEmptyStrings = false)]
    public required string Description { get; init; }
}
