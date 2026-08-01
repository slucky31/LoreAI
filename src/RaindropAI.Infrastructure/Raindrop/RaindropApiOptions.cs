using System.ComponentModel.DataAnnotations;

namespace RaindropAI.Infrastructure.Raindrop;

public sealed class RaindropApiOptions
{
    [Required(AllowEmptyStrings = false)]
    [Url]
    public string BaseUrl { get; init; } = "https://api.raindrop.io/rest/v1";

    [Required(AllowEmptyStrings = false)]
    public required string Token { get; init; }

    /// <summary>-1 = Non trié (traitement principal), 0 = toute la collection hors corbeille.</summary>
    public long CollectionId { get; init; } = -1;

    /// <summary>L'API Raindrop plafonne <c>perpage</c> à 50 ; au-delà, la pagination décrocherait.</summary>
    [Range(1, 50)]
    public int PageSize { get; init; } = 50;
}
