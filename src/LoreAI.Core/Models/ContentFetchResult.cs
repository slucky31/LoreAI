using LoreAI.Core.Enums;

namespace LoreAI.Core.Models;

public sealed record ContentFetchResult(ContentFetchStatus Status, string? Text, int? WordCount)
{
    public static readonly ContentFetchResult Skipped = new(ContentFetchStatus.Skipped, null, null);
}
