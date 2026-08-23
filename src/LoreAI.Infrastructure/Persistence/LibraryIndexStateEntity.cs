namespace LoreAI.Infrastructure.Persistence;

/// <summary>Une ligne par source (clé <see cref="SourceType"/>) — curseur de <c>LibraryIndexingJob</c> (lot 1, #42), distinct de <see cref="PollingStateEntity"/>.</summary>
public sealed class LibraryIndexStateEntity
{
    public required string SourceType { get; set; }
    public int? ResumePage { get; set; }
    public DateTimeOffset? LastFullPassStartedUtc { get; set; }
    public DateTimeOffset? LastFullPassCompletedUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
