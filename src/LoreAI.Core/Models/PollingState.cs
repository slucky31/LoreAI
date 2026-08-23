using LoreAI.Core.Enums;

namespace LoreAI.Core.Models;

/// <summary>Curseur de polling, une instance par source (ADR 0012) : clé = <see cref="SourceType"/>.</summary>
public sealed record PollingState(
    SourceType SourceType,
    string? LastSourceItemId,
    DateTimeOffset? LastCreatedUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public static PollingState Initial(SourceType sourceType) => new(sourceType, null, null, DateTimeOffset.UnixEpoch);
}
