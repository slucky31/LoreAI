using LoreAI.Core.Models;

namespace LoreAI.Core.Interfaces;

public interface IClassifier
{
    Task<ClassificationResult> ClassifyAsync(RaindropItem item, RaindropTaxonomy taxonomy, CancellationToken cancellationToken);
}
