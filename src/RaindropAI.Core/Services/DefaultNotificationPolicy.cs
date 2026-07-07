using RaindropAI.Core.Enums;
using RaindropAI.Core.Interfaces;
using RaindropAI.Core.Models;

namespace RaindropAI.Core.Services;

/// <summary>
/// Règle par défaut : notification immédiate seulement pour les items à tester jugés prioritaires.
/// Seuils injectables pour rester configurables sans toucher à l'appelant.
/// </summary>
public sealed class DefaultNotificationPolicy : INotificationPolicy
{
    private readonly IReadOnlySet<RecommendedAction> _triggerActions;
    private readonly Priority _minimumPriority;

    public DefaultNotificationPolicy(
        IReadOnlySet<RecommendedAction>? triggerActions = null,
        Priority minimumPriority = Priority.Haute)
    {
        _triggerActions = triggerActions ?? new HashSet<RecommendedAction> { RecommendedAction.ATester };
        _minimumPriority = minimumPriority;
    }

    public bool ShouldNotifyImmediately(ClassificationResult classification) =>
        _triggerActions.Contains(classification.Action) && classification.Priority >= _minimumPriority;
}
