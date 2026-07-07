namespace RaindropAI.Core.Models;

public sealed record PollingState(
    long? LastRaindropId,
    DateTimeOffset? LastCreatedUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public static readonly PollingState Initial = new(null, null, DateTimeOffset.UnixEpoch);
}
