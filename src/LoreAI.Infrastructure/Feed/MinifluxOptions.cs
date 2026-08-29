using System.ComponentModel.DataAnnotations;

namespace LoreAI.Infrastructure.Feed;

public sealed class MinifluxOptions
{
    /// <summary>Adresse de l'instance Miniflux auto-hébergée (lot 7, #48) — DNS interne du réseau Docker partagé (ex. <c>http://miniflux:8080</c>), jamais une adresse publique.</summary>
    [Required(AllowEmptyStrings = false)]
    [Url]
    public required string BaseUrl { get; init; }

    /// <summary>Jeton API généré une fois dans l'UI Miniflux (Settings → API Keys) — pas de flux OAuth, un seul jeton statique.</summary>
    [Required(AllowEmptyStrings = false)]
    public required string ApiToken { get; init; }
}
