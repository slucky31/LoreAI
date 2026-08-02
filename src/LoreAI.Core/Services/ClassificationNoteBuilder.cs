using LoreAI.Core.Models;

namespace LoreAI.Core.Services;

/// <summary>
/// Construit le bloc de note déposé sur le raindrop. La note rédigée par l'utilisateur est préservée,
/// mais le bloc d'un passage précédent est <b>remplacé</b>, jamais empilé : la note est relue depuis l'API
/// à chaque cycle, donc un simple append ferait grossir la note à chaque rejeu.
/// Pure, testable sans appel réseau — même esprit que <c>DigestMessageBuilder</c>.
/// </summary>
public static class ClassificationNoteBuilder
{
    public const string Marker = "[LoreAI]";

    public static string Build(string? existingNote, ClassificationResult classification)
    {
        var block = $"{Marker} {classification.Action} — {classification.Priority} — {Collapse(classification.Reason)}";
        var userNote = StripPreviousBlocks(existingNote);

        return userNote.Length == 0 ? block : $"{userNote}\n\n{block}";
    }

    /// <summary>
    /// Retire les lignes déposées par un passage précédent. On filtre ligne à ligne plutôt que de tronquer
    /// à partir du marqueur : l'utilisateur peut avoir écrit sous le bloc, et son texte doit survivre.
    /// </summary>
    private static string StripPreviousBlocks(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return string.Empty;
        }

        var kept = note
            .ReplaceLineEndings("\n")
            .Split('\n')
            .Where(line => !line.TrimStart().StartsWith(Marker, StringComparison.Ordinal));

        return string.Join('\n', kept).Trim();
    }

    /// <summary>
    /// Le bloc doit tenir sur une seule ligne : c'est ce qui garantit qu'on saura le retrouver et le
    /// remplacer au passage suivant, même si le modèle glisse un retour à la ligne dans sa justification.
    /// </summary>
    private static string Collapse(string reason) =>
        string.Join(' ', reason.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
