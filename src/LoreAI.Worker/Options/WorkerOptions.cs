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

    /// <summary>
    /// Actif par défaut : récupère le contenu réel de chaque nouvel article avant classification (S1, lot 4).
    /// Best-effort — un échec ne bloque jamais le cycle. Interrupteur de secours à passer à false si un
    /// domaine pose problème en production, même logique que <see cref="WriteBackToRaindrop"/>.
    /// </summary>
    public bool FetchArticleContent { get; init; } = true;

    /// <summary>
    /// Expression cron de la revue mensuelle narrative (S4, lot 5) — par défaut le 1er de chaque mois à 5h
    /// UTC, une heure après <see cref="WeeklyInsightsCronExpression"/>. Revoit le mois calendaire précédent.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string MonthlyReviewCronExpression { get; init; } = "0 5 1 * *";

    /// <summary>
    /// Expression cron de la réconciliation (L3, lot 6) — par défaut chaque jour à 2h UTC, avant
    /// <see cref="LibraryIndexCronExpression"/> (3h) pour que le rapport hebdomadaire du dimanche lise
    /// un état frais. Quotidien plutôt qu'hebdomadaire : les seuils de relance (L4, 14j) et de
    /// péremption (N4, 90j) restent utiles même si le rapport lui-même reste hebdomadaire.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string ReconciliationCronExpression { get; init; } = "0 2 * * *";

    /// <summary>Expression cron du connecteur Gmail (lot 8, #49) — par défaut chaque heure : les newsletters sont peu fréquentes, pas besoin du rythme 15 min de « Non trié ».</summary>
    [Required(AllowEmptyStrings = false)]
    public string EmailIngestionCronExpression { get; init; } = "0 * * * *";

    /// <summary>
    /// Inactif par défaut (lot 8, #49) : tant qu'aucun client OAuth Google n'est configuré (étape manuelle,
    /// cf. README), le connecteur Gmail ne doit ni être planifié, ni exiger une section <c>Gmail</c>
    /// valide au démarrage — même logique que <see cref="WriteBackToRaindrop"/>.
    /// </summary>
    public bool EmailIngestionEnabled { get; init; }

    /// <summary>Expression cron du connecteur RSS/Miniflux (lot 7, #48) — par défaut chaque heure, même cadence que le connecteur Gmail.</summary>
    [Required(AllowEmptyStrings = false)]
    public string FeedIngestionCronExpression { get; init; } = "0 * * * *";

    /// <summary>
    /// Inactif par défaut (lot 7, #48) : tant qu'aucune instance Miniflux n'est déployée/configurée (étape
    /// manuelle, cf. guide de déploiement), le connecteur Feed ne doit ni être planifié, ni exiger une
    /// section <c>Miniflux</c> valide au démarrage — même logique qu'<see cref="EmailIngestionEnabled"/>.
    /// </summary>
    public bool FeedIngestionEnabled { get; init; }

    /// <summary>
    /// Expression cron du tag hebdomadaire de la file de lecture (L5, lot 8) — par défaut chaque dimanche
    /// à 4h05 UTC, 5 min après <see cref="WeeklyInsightsCronExpression"/> pour une file fraîche cohérente
    /// avec le digest Discord.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string ReadingQueueTaggingCronExpression { get; init; } = "5 4 * * 0";

    /// <summary>
    /// Inactif par défaut (L5, lot 8) : première écriture du projet hors « Non trié » (pose un tag sur des
    /// articles déjà classés et rangés) — un flag dédié explicite, jamais un effet de bord, même logique
    /// que <see cref="WriteBackToRaindrop"/>. N'écrit jamais la note ni ne déplace la collection : seul le
    /// tag est modifié.
    /// </summary>
    public bool ReadingQueueTaggingEnabled { get; init; }

    /// <summary>Nom du tag posé sur les articles de la file de lecture de la semaine (L5, lot 8).</summary>
    [Required(AllowEmptyStrings = false)]
    public string ReadingQueueTagName { get; init; } = "cette-semaine";

    /// <summary>Expression cron de la veille automatique sur sujets (C4, lot 9, #50) — par défaut toutes les 6h : pas besoin du rythme 15 min du cycle Raindrop, et chaque candidat coûte un appel LLM.</summary>
    [Required(AllowEmptyStrings = false)]
    public string TopicWatchCronExpression { get; init; } = "0 */6 * * *";

    /// <summary>
    /// Inactif par défaut (lot 9, #50) : tant qu'aucune catégorie Miniflux de veille n'est configurée (étape
    /// manuelle, cf. README), le job ne doit ni être planifié, ni exiger une section <c>Watch</c> valide au
    /// démarrage — même logique que <see cref="FeedIngestionEnabled"/>.
    /// </summary>
    public bool TopicWatchEnabled { get; init; }
}
