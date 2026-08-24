using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using LoreAI.Core.Enums;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;
using LoreAI.Worker.Services;

namespace LoreAI.Worker.Tests.Services;

/// <summary>
/// Couvre l'orchestration de la revue mensuelle (S4, #46) : le regroupement par thème est déjà couvert,
/// pur et sans I/O, par <c>MonthlyReviewGrouperTests</c> — ici on vérifie l'appel au générateur narratif par
/// thème, l'envoi (ou son absence sur un mois vide), et que le job n'échoue jamais bruyamment.
/// </summary>
public class MonthlyReviewJobTests
{
    [Fact]
    public async Task Invoke_NoClassifiedArticlesThisMonth_DoesNotSendReport()
    {
        var fixture = new JobFixture();

        await fixture.Build().Invoke();

        await fixture.ReportNotifier.DidNotReceive().SendReportAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await fixture.NarrativeGenerator.DidNotReceive().GenerateNarrativeAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<MonthlyReviewArticle>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_ClassifiedArticles_GeneratesOneNarrativePerThemeAndSendsReport()
    {
        var fixture = new JobFixture().WithArticles(
            CreateArticle(1, "Veille .NET"),
            CreateArticle(2, "Veille .NET"),
            CreateArticle(3, "IA"));

        await fixture.Build().Invoke();

        await fixture.NarrativeGenerator.Received(1).GenerateNarrativeAsync("Veille .NET", Arg.Any<IReadOnlyList<MonthlyReviewArticle>>(), Arg.Any<CancellationToken>());
        await fixture.NarrativeGenerator.Received(1).GenerateNarrativeAsync("IA", Arg.Any<IReadOnlyList<MonthlyReviewArticle>>(), Arg.Any<CancellationToken>());
        await fixture.ReportNotifier.Received(1).SendReportAsync(
            Arg.Is<string>(name => name!.StartsWith("loreai-revue-mensuelle-", StringComparison.Ordinal) && name.EndsWith(".md", StringComparison.Ordinal)),
            Arg.Is<string>(markdown => markdown!.Contains("Veille .NET", StringComparison.Ordinal) && markdown.Contains("IA", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_ArticleRepositoryThrows_DoesNotThrowAndDoesNotSendReport()
    {
        var fixture = new JobFixture();
        fixture.ArticleRepository
            .GetClassifiedBetweenAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database is locked"));

        var exception = await Record.ExceptionAsync(() => fixture.Build().Invoke());

        Assert.Null(exception);
        await fixture.ReportNotifier.DidNotReceive().SendReportAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_ReportNotifierThrows_DoesNotThrow()
    {
        var fixture = new JobFixture().WithArticles(CreateArticle(1, "Veille .NET"));
        fixture.ReportNotifier
            .SendReportAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("500"));

        var exception = await Record.ExceptionAsync(() => fixture.Build().Invoke());

        Assert.Null(exception);
    }

    [Fact]
    public async Task Invoke_NarrativeGeneratorThrows_DoesNotThrow()
    {
        var fixture = new JobFixture().WithArticles(CreateArticle(1, "Veille .NET"));
        fixture.NarrativeGenerator
            .GenerateNarrativeAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<MonthlyReviewArticle>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("500"));

        var exception = await Record.ExceptionAsync(() => fixture.Build().Invoke());

        Assert.Null(exception);
    }

    private static MonthlyReviewArticle CreateArticle(long id, string theme) =>
        new(id, $"Titre {id}", $"https://example.com/{id}", theme, [], null, null, Priority.Moyenne);

    private sealed class JobFixture
    {
        public IArticleRepository ArticleRepository { get; } = Substitute.For<IArticleRepository>();
        public IThemeNarrativeGenerator NarrativeGenerator { get; } = Substitute.For<IThemeNarrativeGenerator>();
        public IReportNotifier ReportNotifier { get; } = Substitute.For<IReportNotifier>();

        public JobFixture()
        {
            ArticleRepository
                .GetClassifiedBetweenAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns([]);
            NarrativeGenerator
                .GenerateNarrativeAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<MonthlyReviewArticle>>(), Arg.Any<CancellationToken>())
                .Returns("Narration de test.");
        }

        public JobFixture WithArticles(params MonthlyReviewArticle[] articles)
        {
            ArticleRepository
                .GetClassifiedBetweenAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(articles);
            return this;
        }

        public MonthlyReviewJob Build() => new(
            ArticleRepository,
            NarrativeGenerator,
            ReportNotifier,
            NullLogger<MonthlyReviewJob>.Instance);
    }
}
