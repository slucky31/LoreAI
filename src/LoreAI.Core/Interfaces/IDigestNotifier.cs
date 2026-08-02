using LoreAI.Core.Models;

namespace LoreAI.Core.Interfaces;

/// <summary>Notification groupée, envoyée périodiquement (ex. digest email quotidien).</summary>
public interface IDigestNotifier
{
    Task SendDigestAsync(IReadOnlyList<ClassifiedArticle> articles, CancellationToken cancellationToken);
}
