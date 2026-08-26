using System.ComponentModel.DataAnnotations;

namespace LoreAI.Infrastructure.Gmail;

public sealed class GoogleOAuthOptions
{
    [Required(AllowEmptyStrings = false)]
    public required string ClientId { get; init; }

    [Required(AllowEmptyStrings = false)]
    public required string ClientSecret { get; init; }

    /// <summary>Obtenu une fois par consentement interactif hors LoreAI (étape manuelle, cf. README) : le worker ne fait jamais de flux OAuth interactif.</summary>
    [Required(AllowEmptyStrings = false)]
    public required string RefreshToken { get; init; }

    /// <summary>Nom du label Gmail posé en amont par un filtre de l'utilisateur — LoreAI ne trie pas lui-même le inbox, il fait confiance à ce label.</summary>
    [Required(AllowEmptyStrings = false)]
    public required string Label { get; init; }

    [Required(AllowEmptyStrings = false)]
    [Url]
    public string TokenUrl { get; init; } = "https://oauth2.googleapis.com/token";

    [Required(AllowEmptyStrings = false)]
    [Url]
    public string ApiBaseUrl { get; init; } = "https://gmail.googleapis.com/gmail/v1/";
}
