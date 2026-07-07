using RaindropAI.Core.Models;

namespace RaindropAI.Core.Interfaces;

public interface IPollingStateRepository
{
    Task<PollingState> GetAsync(CancellationToken cancellationToken);

    Task UpdateAsync(PollingState state, CancellationToken cancellationToken);
}
