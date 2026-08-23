using LoreAI.Core.Models;

namespace LoreAI.Core.Interfaces;

public interface ICycleRunRepository
{
    Task RecordAsync(CycleRun run, CancellationToken cancellationToken);

    /// <summary>Les cycles les plus récents, du plus récent au plus ancien — alimente le healthcheck.</summary>
    Task<IReadOnlyList<CycleRun>> GetRecentAsync(int count, CancellationToken cancellationToken);
}
