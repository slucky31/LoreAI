using Microsoft.Extensions.Http.Resilience;

namespace LoreAI.Worker.Resilience;

/// <summary>
/// Calibre la résilience HTTP du fetch de contenu d'article (S1, lot 4). Contrairement à
/// <see cref="ClassifierResilience"/> (appel LLM, tolérant à l'attente), il s'agit de requêtes vers des
/// sites tiers inconnus : la politesse impose un timeout court et pas de retry agressif — un site lent
/// ou en panne ne doit pas retarder le cycle, l'article retombe simplement sur son excerpt Raindrop.
/// </summary>
public static class ContentFetchResilience
{
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(8);
    public static readonly TimeSpan TotalRequestTimeout = TimeSpan.FromSeconds(15);

    public static void Configure(HttpStandardResilienceOptions options)
    {
        options.AttemptTimeout.Timeout = AttemptTimeout;
        options.TotalRequestTimeout.Timeout = TotalRequestTimeout;
        options.Retry.MaxRetryAttempts = 1;

        // Même contrainte que ClassifierResilience : la fenêtre du disjoncteur doit valoir au moins deux
        // fois le timeout par tentative.
        options.CircuitBreaker.SamplingDuration = AttemptTimeout * 2;
    }
}
