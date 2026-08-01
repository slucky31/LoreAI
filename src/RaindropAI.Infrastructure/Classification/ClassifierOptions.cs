using System.ComponentModel.DataAnnotations;

namespace RaindropAI.Infrastructure.Classification;

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
}
