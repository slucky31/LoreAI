using RaindropAI.Core.Models;

namespace RaindropAI.Core.Interfaces;

public interface INotificationPolicy
{
    bool ShouldNotifyImmediately(ClassificationResult classification);
}
