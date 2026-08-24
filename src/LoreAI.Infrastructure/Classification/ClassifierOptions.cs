using System.ComponentModel.DataAnnotations;

namespace LoreAI.Infrastructure.Classification;

public sealed class ClassifierOptions
{
    [Required(AllowEmptyStrings = false)]
    public required string ApiKey { get; init; }

    [Required(AllowEmptyStrings = false)]
    public string Model { get; init; } = "claude-haiku-4-5";

    [Required(AllowEmptyStrings = false)]
    [Url]
    public string BaseUrl { get; init; } = "https://api.anthropic.com";

    [Required(AllowEmptyStrings = false)]
    public string AnthropicVersion { get; init; } = "2023-06-01";

    /// <summary>
    /// Point d'extension pour un modèle dédié au résumé (D6, lot 4) — le choix reste ouvert. Non consommé
    /// aujourd'hui : <c>summary</c> part dans le même appel tool-use que la classification (<see cref="Model"/>),
    /// pas dans un second appel séparé.
    /// </summary>
    public string? SummaryModel { get; init; }
}
