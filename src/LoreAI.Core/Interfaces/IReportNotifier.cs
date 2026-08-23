namespace LoreAI.Core.Interfaces;

/// <summary>Envoie un rapport Markdown en pièce jointe (ex. le rapport hebdomadaire d'insights, #43).</summary>
public interface IReportNotifier
{
    Task SendReportAsync(string fileName, string markdownContent, CancellationToken cancellationToken);
}
