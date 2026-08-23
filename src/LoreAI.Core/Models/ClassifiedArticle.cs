namespace LoreAI.Core.Models;

public sealed record ClassifiedArticle(
    Item Item,
    ClassificationResult Classification,
    DateTimeOffset ClassifiedAtUtc,
    bool Moved,
    DateTimeOffset? DiscordNotifiedAtUtc,
    DateTimeOffset? EmailDigestSentAtUtc,
    DateTimeOffset FetchedAtUtc,
    string? WriteBackStatus);
