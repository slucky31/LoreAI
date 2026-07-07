namespace RaindropAI.Worker.Options;

public sealed class WorkerOptions
{
    /// <summary>Expression cron du cycle de polling Raindrop (par défaut toutes les 15 minutes).</summary>
    public string PollingCronExpression { get; init; } = "*/15 * * * *";

    /// <summary>Expression cron d'envoi du digest email (par défaut tous les jours à 7h UTC).</summary>
    public string DigestCronExpression { get; init; } = "0 7 * * *";

    /// <summary>Désactivé par défaut : écrit le résultat de classification (tag + note) dans Raindrop.</summary>
    public bool WriteBackToRaindrop { get; init; } = false;
}
