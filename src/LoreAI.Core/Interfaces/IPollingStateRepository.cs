using LoreAI.Core.Models;

namespace LoreAI.Core.Interfaces;

public interface IPollingStateRepository
{
    Task<PollingState> GetAsync(CancellationToken cancellationToken);

    Task UpdateAsync(PollingState state, CancellationToken cancellationToken);
}
