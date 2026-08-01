using RaindropAI.Core.Enums;
using RaindropAI.Core.Interfaces;
using RaindropAI.Core.Models;

namespace RaindropAI.Core.Services;

/// <summary>
/// Règle par défaut : notification immédiate seulement pour les items à tester jugés prioritaires.
/// Les seuils sont alimentés depuis la configuration par le Worker (section <c>Notification</c>) ; ils
/// restent des paramètres de constructeur simples pour que Core n'ait à dépendre d'aucun système d'options.
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
