namespace LoreAI.Core.Models;

public sealed record RaindropItem(
    long Id,
    string Title,
    string Link,
    string? Excerpt,
    string? Note,
    IReadOnlyList<string> Tags,
    long? CollectionId,
    string? Domain,
    string? RaindropType,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? LastUpdateUtc);
