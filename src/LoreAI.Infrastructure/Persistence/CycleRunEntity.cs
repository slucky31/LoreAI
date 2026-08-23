using LoreAI.Core.Enums;

namespace LoreAI.Infrastructure.Persistence;

/// <summary>
/// Forme persistée d'un <c>CycleRun</c>. Contrairement à <see cref="ArticleEntity"/>/<see cref="PollingStateEntity"/>,
/// il n'y a pas de clé applicative (rien n'identifie un cycle a priori) : <see cref="Id"/> est généré par la base.
/// </summary>
public sealed class CycleRunEntity
{
    public long Id { get; set; }
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset CompletedUtc { get; set; }
    public CycleOutcome Outcome { get; set; }
    public int ItemsSeen { get; set; }
    public int ItemsProcessed { get; set; }
    public int Moved { get; set; }
    public int TagsApplied { get; set; }
    public int Notified { get; set; }
    public string? FailureReason { get; set; }
}
