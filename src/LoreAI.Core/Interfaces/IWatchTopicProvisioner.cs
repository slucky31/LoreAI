using LoreAI.Core.Models;

namespace LoreAI.Core.Interfaces;

/// <summary>
/// Provisionne un nouveau sujet de veille (C4, lot 9, #50) : crée la collection Raindrop et la catégorie
/// Miniflux dédiées, retourne un <see cref="WatchTopic"/> prêt à persister (curseur non initialisé —
/// l'appelant décide de la valeur de seed, cf. <c>--add-watch-topic</c>).
/// </summary>
public interface IWatchTopicProvisioner
{
    Task<WatchTopic> ProvisionAsync(string name, string description, CancellationToken cancellationToken);
}
