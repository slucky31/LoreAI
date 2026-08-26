using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;
using LoreAI.Worker.Services;

namespace LoreAI.Worker.Tests.Services;

/// <summary>
/// Couvre l'orchestration du rapport hebdomadaire (#43) : la logique de calcul de chaque insight est déjà
/// couverte, pure et sans I/O, par les tests de <c>LoreAI.Core.Tests.Services</c> — ici on vérifie que le
/// job assemble et envoie, et surtout qu'il n'échoue jamais bruyamment.
/// </summary>
public class WeeklyInsightsJobTests
{
    private static readonly RaindropTaxonomy EmptyTaxonomy = new([], []);

    [Fact]
    public async Task Invoke_HappyPath_SendsMarkdownReportOnce()
    {
        var fixture = new JobFixture();

        await fixture.Build().Invoke();

        await fixture.ReportNotifier.Received(1).SendReportAsync(
            Arg.Is<string>(name => name!.StartsWith("loreai-insights-", StringComparison.Ordinal) && name.EndsWith(".md", StringComparison.Ordinal)),
            Arg.Is<string>(markdown => markdown!.Contains("Doublons d'URL", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_LibraryRepositoryThrows_DoesNotThrowAndDoesNotSendReport()
    {
        var fixture = new JobFixture();
        fixture.LibraryItemRepository
            .GetAllForInsightsAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database is locked"));

        var exception = await Record.ExceptionAsync(() => fixture.Build().Invoke());

        Assert.Null(exception);
        await fixture.ReportNotifier.DidNotReceive().SendReportAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_ReportNotifierThrows_DoesNotThrow()
    {
        var fixture = new JobFixture();
        fixture.ReportNotifier
            .SendReportAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("500"));

        var exception = await Record.ExceptionAsync(() => fixture.Build().Invoke());

        Assert.Null(exception);
    }

    /// <summary>S6 (lot 8) : le coût LLM combine désormais la classification et l'extraction de liens Newsletter dans un total unique.</summary>
    [Fact]
    public async Task Invoke_CombinesClassificationAndEmailExtractionUsage()
    {
        var fixture = new JobFixture();
        fixture.ArticleRepository
            .GetClassificationRawResponsesSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(["{\"usage\":{\"input_tokens\":100,\"output_tokens\":10}}"]);
        fixture.EmailExtractionLogRepository
            .GetRawResponsesSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(["{\"usage\":{\"input_tokens\":50,\"output_tokens\":5}}"]);

        await fixture.Build().Invoke();

        await fixture.ReportNotifier.Received(1).SendReportAsync(
            Arg.Any<string>(),
            Arg.Is<string>(markdown => markdown!.Contains("150 / 15", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_UsesFreshlyLearnedTaxonomyForTagAndCollectionInsights()
    {
        var fixture = new JobFixture()
            .WithTaxonomy(new RaindropTaxonomy([new RaindropCollection(10, "Veille")], [new RaindropTag("dotnet", 1)]))
            .WithLibraryItems(new LibraryItemSummary(1, "A", "https://example.com/a", [], 10, DateTimeOffset.UtcNow));

        await fixture.Build().Invoke();

        await fixture.ReportNotifier.Received(1).SendReportAsync(
            Arg.Any<string>(),
            Arg.Is<string>(markdown => markdown!.Contains("Veille", StringComparison.Ordinal) && markdown.Contains("dotnet", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    private sealed class JobFixture
    {
        public ILibraryItemRepository LibraryItemRepository { get; } = Substitute.For<ILibraryItemRepository>();
        public IArticleRepository ArticleRepository { get; } = Substitute.For<IArticleRepository>();
        public IEmailExtractionLogRepository EmailExtractionLogRepository { get; } = Substitute.For<IEmailExtractionLogRepository>();
        public IRaindropClient RaindropClient { get; } = Substitute.For<IRaindropClient>();
        public IReportNotifier ReportNotifier { get; } = Substitute.For<IReportNotifier>();

        public JobFixture()
        {
            LibraryItemRepository.GetAllForInsightsAsync(Arg.Any<CancellationToken>()).Returns([]);
            RaindropClient.GetTaxonomyAsync(Arg.Any<CancellationToken>()).Returns(EmptyTaxonomy);
            ArticleRepository.GetClassificationRawResponsesSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);
            ArticleRepository.GetTrackedArticlesAsync(Arg.Any<CancellationToken>()).Returns([]);
            EmailExtractionLogRepository.GetRawResponsesSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);
        }

        public JobFixture WithTaxonomy(RaindropTaxonomy taxonomy)
        {
            RaindropClient.GetTaxonomyAsync(Arg.Any<CancellationToken>()).Returns(taxonomy);
            return this;
        }

        public JobFixture WithLibraryItems(params LibraryItemSummary[] items)
        {
            LibraryItemRepository.GetAllForInsightsAsync(Arg.Any<CancellationToken>()).Returns(items);
            return this;
        }

        public WeeklyInsightsJob Build() => new(
            LibraryItemRepository,
            ArticleRepository,
            EmailExtractionLogRepository,
            RaindropClient,
            ReportNotifier,
            NullLogger<WeeklyInsightsJob>.Instance);
    }
}
