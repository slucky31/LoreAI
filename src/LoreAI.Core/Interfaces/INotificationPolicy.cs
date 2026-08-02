using LoreAI.Core.Models;

namespace LoreAI.Core.Interfaces;

public interface INotificationPolicy
{
    bool ShouldNotifyImmediately(ClassificationResult classification);
}
