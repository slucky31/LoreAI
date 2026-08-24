namespace LoreAI.Core.Services;

/// <summary>Corollaire gratuit de S1 (lot 4) : temps de lecture estimé à partir du nombre de mots. Pure, sans I/O.</summary>
public static class ReadingTimeEstimator
{
    private const int WordsPerMinute = 220;

    public static int? EstimateMinutes(int? wordCount) =>
        wordCount is null or <= 0 ? null : (int)Math.Ceiling(wordCount.Value / (double)WordsPerMinute);
}
