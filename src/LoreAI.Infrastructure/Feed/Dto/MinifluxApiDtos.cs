using System.Text.Json.Serialization;

namespace LoreAI.Infrastructure.Feed.Dto;

/// <summary>Réponse de <c>GET /v1/entries</c> (API Miniflux, JSON snake_case).</summary>
internal sealed class EntriesResponseDto
{
    public List<EntryDto> Entries { get; set; } = [];
}

internal sealed class EntryDto
{
    public long Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("published_at")]
    public DateTimeOffset PublishedAt { get; set; }
}
