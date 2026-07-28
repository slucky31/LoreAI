namespace RaindropAI.Infrastructure.Raindrop;

public sealed class RaindropApiOptions
{
    public string BaseUrl { get; init; } = "https://api.raindrop.io/rest/v1";
    public required string Token { get; init; }

    /// <summary>-1 = Non trié (traitement principal), 0 = toute la collection hors corbeille.</summary>
    public long CollectionId { get; init; } = -1;

    public int PageSize { get; init; } = 50;
}
