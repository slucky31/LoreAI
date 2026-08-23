using System.ComponentModel.DataAnnotations;

namespace LoreAI.Worker.Options;

public sealed class WorkerOptions
{
    /// <summary>Expression cron du cycle de polling Raindrop (par défaut toutes les 15 minutes).</summary>
    [Required(AllowEmptyStrings = false)]
    public string PollingCronExpression { get; init; } = "*/15 * * * *";

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

    /// <summary>Expression cron de l'indexation en lecture seule de toute la bibliothèque (lot 1, #42) — déclenchement rare, par défaut chaque dimanche à 3h UTC.</summary>
    [Required(AllowEmptyStrings = false)]
    public string LibraryIndexCronExpression { get; init; } = "0 3 * * 0";

    /// <summary>Inactif par défaut : lance une passe d'indexation de la bibliothèque au démarrage du worker (soumise à la garde des 24h de <c>LibraryIndexingJob</c>).</summary>
    public bool IndexLibraryOnStartup { get; init; }

    /// <summary>
    /// Expression cron du rapport hebdomadaire d'insights (lot 2, #43) — par défaut chaque dimanche à 4h
    /// UTC, une heure après <see cref="LibraryIndexCronExpression"/> pour lire une bibliothèque fraîchement
    /// réindexée.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string WeeklyInsightsCronExpression { get; init; } = "0 4 * * 0";
}
