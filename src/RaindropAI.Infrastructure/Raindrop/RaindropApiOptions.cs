namespace RaindropAI.Infrastructure.Raindrop;

public sealed class RaindropApiOptions
{
    public string BaseUrl { get; init; } = "https://api.raindrop.io/rest/v1";
    public required string Token { get; init; }
    public long CollectionId { get; init; } = 0;
    public int PageSize { get; init; } = 50;
}
