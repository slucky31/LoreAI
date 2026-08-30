using System.Text.Json;
using System.Text.Json.Serialization;

namespace LoreAI.Infrastructure.Raindrop.Dto;

internal sealed class RaindropsPageDto
{
    public bool Result { get; set; }
    public List<RaindropDto> Items { get; set; } = [];
    public int Count { get; set; }
}

/// <summary>Réponse de <c>GET /raindrop/{id}</c> — un seul item, pas une page (L3, lot 6).</summary>
internal sealed class RaindropItemResponseDto
{
    public bool Result { get; set; }
    public RaindropDto? Item { get; set; }
}

internal sealed class RaindropDto
{
    [JsonPropertyName("_id")]
    public long Id { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Link { get; set; } = string.Empty;
    public string? Excerpt { get; set; }
    public string? Note { get; set; }
    public List<string> Tags { get; set; } = [];
    public RaindropCollectionRefDto? Collection { get; set; }
    public string? Domain { get; set; }
    public string? Type { get; set; }
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset? LastUpdate { get; set; }

    // Ignorés jusqu'ici (n'entraient pas dans le pipeline de classification) ; utilisés par le
    // balayage en lecture seule de toute la bibliothèque (lot 1, #42), voir MapToLibraryItem.
    public bool Broken { get; set; }
    public bool Important { get; set; }
    public string? Cover { get; set; }
    public JsonElement? Highlights { get; set; }
}

internal sealed class RaindropCollectionRefDto
{
    [JsonPropertyName("$id")]
    public long Id { get; set; }
}

internal sealed class CollectionsPageDto
{
    public bool Result { get; set; }
    public List<CollectionDto> Items { get; set; } = [];
}

/// <summary>Réponse de <c>POST /collection</c> — un seul item créé, pas une page (lot 9, #50).</summary>
internal sealed class CollectionItemResponseDto
{
    public bool Result { get; set; }
    public CollectionDto? Item { get; set; }
}

internal sealed class CollectionDto
{
    [JsonPropertyName("_id")]
    public long Id { get; set; }

    public string Title { get; set; } = string.Empty;
}

internal sealed class TagsPageDto
{
    public bool Result { get; set; }
    public List<TagDto> Items { get; set; } = [];
}

internal sealed class TagDto
{
    [JsonPropertyName("_id")]
    public string Id { get; set; } = string.Empty;

    public int Count { get; set; }
}
