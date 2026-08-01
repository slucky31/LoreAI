using System.ComponentModel.DataAnnotations;
using RaindropAI.Core.Enums;

namespace RaindropAI.Worker.Options;

/// <summary>
/// Seuil de déclenchement de la notification immédiate (Discord). Vit dans le Worker et non dans Core :
/// c'est ici qu'on a le droit de dépendre de la configuration, <c>RaindropAI.Core</c> devant rester sans
/// aucune dépendance externe (ADR 0001).
/// </summary>
public sealed class NotificationOptions
{
    /// <summary>Actions qui déclenchent une alerte immédiate. Ex. <c>Notification__TriggerActions__0=ATester</c>.</summary>
    [MinLength(1)]
    public IReadOnlyList<RecommendedAction> TriggerActions { get; init; } = [RecommendedAction.ATester];

    /// <summary>Priorité minimale requise, en plus de l'action.</summary>
    [EnumDataType(typeof(Priority))]
    public Priority MinimumPriority { get; init; } = Priority.Haute;
}
