namespace RaindropAI.Core.Models;

public sealed record ClassifiedArticle(
    RaindropItem Item,
    ClassificationResult Classification,
    DateTimeOffset ClassifiedAtUtc,
    DateTimeOffset? DiscordNotifiedAtUtc,
    DateTimeOffset? EmailDigestSentAtUtc);
