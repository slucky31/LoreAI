namespace LoreAI.Core.Models;

public sealed record ClassifiedArticle(
    RaindropItem Item,
    ClassificationResult Classification,
    DateTimeOffset ClassifiedAtUtc,
    bool Moved,
    DateTimeOffset? DiscordNotifiedAtUtc,
    DateTimeOffset? EmailDigestSentAtUtc);
