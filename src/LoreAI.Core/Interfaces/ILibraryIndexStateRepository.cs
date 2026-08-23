using LoreAI.Core.Enums;
using LoreAI.Core.Models;

namespace LoreAI.Core.Interfaces;

public interface ILibraryIndexStateRepository
{
    Task<LibraryIndexState> GetAsync(SourceType sourceType, CancellationToken cancellationToken);

    Task UpdateAsync(LibraryIndexState state, CancellationToken cancellationToken);
}
