using LoreAI.Core.Enums;
using LoreAI.Core.Models;

namespace LoreAI.Core.Interfaces;

public interface IPollingStateRepository
{
    Task<PollingState> GetAsync(SourceType sourceType, CancellationToken cancellationToken);

    Task UpdateAsync(PollingState state, CancellationToken cancellationToken);
}
