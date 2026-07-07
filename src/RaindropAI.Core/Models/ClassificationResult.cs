using RaindropAI.Core.Enums;

namespace RaindropAI.Core.Models;

public sealed record ClassificationResult(
    Category Category,
    RecommendedAction Action,
    Priority Priority,
    string Reason,
    string Model,
    string RawResponse)
{
    public static ClassificationResult Fallback(string model, string reason, string rawResponse) =>
        new(Category.Autre, RecommendedAction.Reference, Priority.Basse, reason, model, rawResponse);
}
