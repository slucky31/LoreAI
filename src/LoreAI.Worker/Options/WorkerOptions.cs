using System.ComponentModel.DataAnnotations;

namespace LoreAI.Worker.Options;

public sealed class WorkerOptions
{
    /// <summary>Expression cron du cycle de polling Raindrop (par défaut toutes les 15 minutes).</summary>
    [Required(AllowEmptyStrings = false)]
    public string PollingCronExpression { get; init; } = "*/15 * * * *";

    /// <summary>Expression cron d'envoi du digest email (par défaut tous les jours à 7h UTC).</summary>
    [Required(AllowEmptyStrings = false)]
    public string DigestCronExpression { get; init; } = "0 7 * * *";

    /// <summary>
    /// Actif par défaut : applique automatiquement les tags (fusionnés) et déplace le raindrop
    /// vers la collection suggérée quand elle correspond à une collection existante. Aucune étape
    /// de validation humaine — passer à false pour un mode « à blanc » (classification + rapport
    /// seulement, sans toucher à Raindrop).
    /// </summary>
    public bool WriteBackToRaindrop { get; init; } = true;

    /// <summary>
    /// Âge maximal (en minutes) d'un cycle terminé avant que le healthcheck Docker (#35) ne considère le
    /// worker en panne. Défaut : 3× le cron par défaut (15 min), pour absorber un cycle occasionnellement lent.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int HealthMaxCycleAgeMinutes { get; init; } = 45;
}
