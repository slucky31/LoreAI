using LoreAI.Core.Models;

namespace LoreAI.Core.Interfaces;

/// <summary>Notification item par item, déclenchée immédiatement après classification (ex. Discord).</summary>
public interface IImmediateNotifier
{
    Task NotifyAsync(Item item, ClassificationResult classification, CancellationToken cancellationToken);
}
