using Microsoft.Extensions.Http.Resilience;

namespace LoreAI.Worker.Resilience;

/// <summary>
/// Calibre la résilience HTTP de l'appel LLM. Les valeurs par défaut de
/// <c>AddStandardResilienceHandler</c> (10 s par tentative, 30 s au total) conviennent à Raindrop et
/// Discord, mais pas à une génération de modèle : une réponse dépassant 10 s serait annulée puis rejouée,
/// alors que l'appel initial a bel et bien été facturé côté Anthropic, jusqu'à épuiser le budget total
/// et retomber sur un repli.
/// <para>
/// Le reste des valeurs par défaut est conservé volontairement : le retry ne se déclenche que sur 5xx,
/// 408, 429 et erreurs réseau — jamais sur un 400, qui traduirait un schéma d'outil invalide et ne
/// passerait donc jamais — et il respecte l'en-tête <c>Retry-After</c> renvoyé sur 429.
/// </para>
/// </summary>
public static class ClassifierResilience
{
    /// <summary>Large au regard d'un appel Haiku typique (quelques secondes), pour ne pas rejouer inutilement.</summary>
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Plafond de l'ensemble tentatives + attentes ; au-delà, l'article part en repli (F-01).</summary>
    public static readonly TimeSpan TotalRequestTimeout = TimeSpan.FromMinutes(3);

    public static void Configure(HttpStandardResilienceOptions options)
    {
        options.AttemptTimeout.Timeout = AttemptTimeout;
        options.TotalRequestTimeout.Timeout = TotalRequestTimeout;

        // Contrainte du validateur intégré : la fenêtre d'échantillonnage du disjoncteur doit valoir au
        // moins deux fois le timeout par tentative. La laisser à son défaut de 30 s ferait échouer le
        // démarrage dès qu'on allonge AttemptTimeout.
        options.CircuitBreaker.SamplingDuration = AttemptTimeout * 2;
    }
}
