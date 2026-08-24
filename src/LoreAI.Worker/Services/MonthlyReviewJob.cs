using System.Globalization;
using Coravel.Invocable;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;
using LoreAI.Core.Services;
using LoreAI.Infrastructure.Notifications;

namespace LoreAI.Worker.Services;

/// <summary>
/// Revue mensuelle narrative (S4, lot 5) : regroupe les articles classifiés du mois calendaire précédent par
/// thème (collection suggérée), un appel LLM par thème pour une narration, envoi Markdown en pièce jointe
/// Discord — même mécanique que <see cref="WeeklyInsightsJob"/>. Mois sans article classifié : rien à
/// raconter, aucun envoi (même logique « pas d'import, pas de notification » que le compte-rendu de cycle).
/// </summary>
public sealed class MonthlyReviewJob : IInvocable, ICancellableInvocable
{
    private readonly IArticleRepository _articleRepository;
    private readonly IThemeNarrativeGenerator _narrativeGenerator;
    private readonly IReportNotifier _reportNotifier;
    private readonly ILogger<MonthlyReviewJob> _logger;

    public MonthlyReviewJob(
        IArticleRepository articleRepository,
        IThemeNarrativeGenerator narrativeGenerator,
        IReportNotifier reportNotifier,
        ILogger<MonthlyReviewJob> logger)
    {
        _articleRepository = articleRepository;
        _narrativeGenerator = narrativeGenerator;
        _reportNotifier = reportNotifier;
        _logger = logger;
    }

    /// <summary>Alimenté par Coravel, annulé à l'arrêt de l'application (SIGTERM, <c>docker compose down</c>).</summary>
    public CancellationToken CancellationToken { get; set; }

    public async Task Invoke()
    {
        var cancellationToken = CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var periodStartUtc = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(-1);
        var periodEndUtc = periodStartUtc.AddMonths(1);

        try
        {
            var articles = await _articleRepository.GetClassifiedBetweenAsync(periodStartUtc, periodEndUtc, cancellationToken);
            if (articles.Count == 0)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation(
                        "Aucun article classifié entre {Start} et {End} — pas de revue mensuelle à envoyer.",
                        periodStartUtc,
                        periodEndUtc);
                }

                return;
            }

            var themeGroups = MonthlyReviewGrouper.GroupByTheme(articles);
            var themes = new List<ThemeReview>(themeGroups.Count);
            foreach (var (theme, themeArticles) in themeGroups)
            {
                var narrative = await _narrativeGenerator.GenerateNarrativeAsync(theme, themeArticles, cancellationToken);
                themes.Add(new ThemeReview(theme, narrative, themeArticles));
            }

            var report = new MonthlyReviewReport(periodStartUtc, periodEndUtc, themes, now);
            var markdown = MarkdownReportBuilder.BuildMonthlyReview(report);
            var fileName = string.Create(CultureInfo.InvariantCulture, $"loreai-revue-mensuelle-{periodStartUtc:yyyy-MM}.md");
            await _reportNotifier.SendReportAsync(fileName, markdown, cancellationToken);

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Revue mensuelle envoyée : {ArticleCount} articles, {ThemeCount} thèmes.",
                    articles.Count,
                    themes.Count);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Revue mensuelle interrompue par l'arrêt de l'application.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec du calcul ou de l'envoi de la revue mensuelle.");
        }
    }
}
