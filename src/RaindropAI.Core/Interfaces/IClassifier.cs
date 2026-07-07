using RaindropAI.Core.Models;

namespace RaindropAI.Core.Interfaces;

public interface IClassifier
{
    Task<ClassificationResult> ClassifyAsync(RaindropItem item, CancellationToken cancellationToken);
}
