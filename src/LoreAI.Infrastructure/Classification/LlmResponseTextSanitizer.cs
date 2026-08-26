namespace LoreAI.Infrastructure.Classification;

/// <summary>
/// Nettoyage de texte libre partagé par les parseurs de sortie tool-use (<see cref="ClassificationResponseParser"/>,
/// <see cref="EmailLinkExtractionResponseParser"/>) : rien de ce que renvoie le modèle ne doit se déverser tel
/// quel dans les données de l'utilisateur (cf. F-11).
/// </summary>
internal static class LlmResponseTextSanitizer
{
    public static string StripCodeFences(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewLine = trimmed.IndexOf('\n');
        trimmed = firstNewLine >= 0 ? trimmed[(firstNewLine + 1)..] : trimmed;

        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (lastFence >= 0)
        {
            trimmed = trimmed[..lastFence];
        }

        return trimmed.Trim();
    }

    /// <summary>Aplatit les blancs (un tag/titre ne tient que sur une ligne), retire les caractères de contrôle et tronque.</summary>
    public static string? SanitizeFreeText(string? raw, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var collapsed = string.Join(' ', raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var printable = new string(collapsed.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (printable.Length == 0)
        {
            return null;
        }

        return printable.Length <= maxLength ? printable : printable[..maxLength].TrimEnd();
    }
}
