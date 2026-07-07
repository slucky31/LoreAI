using System.Text.Json.Serialization;

namespace RaindropAI.Infrastructure.Raindrop.Dto;

internal sealed class RaindropsPageDto
{
    public bool Result { get; set; }
    public List<RaindropDto> Items { get; set; } = [];
    public int Count { get; set; }
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
}

internal sealed class RaindropCollectionRefDto
{
    [JsonPropertyName("$id")]
    public long Id { get; set; }
}
