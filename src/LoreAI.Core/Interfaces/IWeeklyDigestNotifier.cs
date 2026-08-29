using LoreAI.Core.Models;

namespace LoreAI.Core.Interfaces;

/// <summary>Envoie le rapport hebdomadaire d'insights sous forme de digest Discord natif (embeds), plutôt qu'en pièce jointe Markdown (O6, #78).</summary>
public interface IWeeklyDigestNotifier
{
    Task SendDigestAsync(WeeklyInsightsReport report, CancellationToken cancellationToken);
}
