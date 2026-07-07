namespace RaindropAI.Infrastructure.Classification;

public sealed class ClassifierOptions
{
    public required string ApiKey { get; init; }
    public string Model { get; init; } = "claude-haiku-4-5";
    public string BaseUrl { get; init; } = "https://api.anthropic.com";
    public string AnthropicVersion { get; init; } = "2023-06-01";
}
