namespace LoreAI.Core.Interfaces;

/// <summary>
/// Relance (L4, lot 6) : un article <c>ATester</c>/<c>Haute</c> jamais traité 14 jours après sa
/// classification. Signature dédiée plutôt que <see cref="IImmediateNotifier"/> : cette relance part
/// d'un article persisté (<c>ReconciliationJob</c>), pas d'un <c>Item</c>/<c>ClassificationResult</c>
/// frais issu du pipeline de classification.
/// </summary>
public interface IReminderNotifier
{
    Task NotifyAsync(string title, string url, int daysSinceClassified, CancellationToken cancellationToken);
}
