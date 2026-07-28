using RaindropAI.Core.Enums;

namespace RaindropAI.Core.Models;

public sealed record ClassificationResult(
    string? SuggestedCollection,
    IReadOnlyList<string> Tags,
    RecommendedAction Action,
    Priority Priority,
    string Reason,
    string Model,
    string RawResponse)
{
    public static ClassificationResult Fallback(string model, string reason, string rawResponse) =>
        new(null, [], RecommendedAction.Reference, Priority.Basse, reason, model, rawResponse);
}
