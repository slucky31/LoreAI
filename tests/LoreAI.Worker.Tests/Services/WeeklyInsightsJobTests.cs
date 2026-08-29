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
    public async Task Invoke_HappyPath_SendsDigestOnce()
    {
        var fixture = new JobFixture();

        await fixture.Build().Invoke();

        await fixture.WeeklyDigestNotifier.Received(1).SendDigestAsync(Arg.Any<WeeklyInsightsReport>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_LibraryRepositoryThrows_DoesNotThrowAndDoesNotSendDigest()
    {
        var fixture = new JobFixture();
        fixture.LibraryItemRepository
            .GetAllForInsightsAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database is locked"));

        var exception = await Record.ExceptionAsync(() => fixture.Build().Invoke());

        Assert.Null(exception);
        await fixture.WeeklyDigestNotifier.DidNotReceive().SendDigestAsync(Arg.Any<WeeklyInsightsReport>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_WeeklyDigestNotifierThrows_DoesNotThrow()
    {
        var fixture = new JobFixture();
        fixture.WeeklyDigestNotifier
            .SendDigestAsync(Arg.Any<WeeklyInsightsReport>(), Arg.Any<CancellationToken>())
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

        await fixture.WeeklyDigestNotifier.Received(1).SendDigestAsync(
            Arg.Is<WeeklyInsightsReport>(r => r!.LlmUsage.InputTokens == 150 && r.LlmUsage.OutputTokens == 15),
            Arg.Any<CancellationToken>());
    }

    /// <summary>S6 (lot 9, #50) : le coût LLM combine aussi les évaluations de veille dans le total unique.</summary>
    [Fact]
    public async Task Invoke_CombinesWatchEvaluationUsage()
    {
        var fixture = new JobFixture();
        fixture.ArticleRepository
            .GetClassificationRawResponsesSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(["{\"usage\":{\"input_tokens\":100,\"output_tokens\":10}}"]);
        fixture.WatchEvaluationLogRepository
            .GetRawResponsesSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(["{\"usage\":{\"input_tokens\":20,\"output_tokens\":2}}"]);

        await fixture.Build().Invoke();

        await fixture.WeeklyDigestNotifier.Received(1).SendDigestAsync(
            Arg.Is<WeeklyInsightsReport>(r => r!.LlmUsage.InputTokens == 120 && r.LlmUsage.OutputTokens == 12),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_UsesFreshlyLearnedTaxonomyForTagAndCollectionInsights()
    {
        var fixture = new JobFixture()
            .WithTaxonomy(new RaindropTaxonomy([new RaindropCollection(10, "Veille")], [new RaindropTag("dotnet", 1)]))
            .WithLibraryItems(new LibraryItemSummary(1, "A", "https://example.com/a", [], 10, DateTimeOffset.UtcNow));

        await fixture.Build().Invoke();

        await fixture.WeeklyDigestNotifier.Received(1).SendDigestAsync(
            Arg.Is<WeeklyInsightsReport>(r => r!.UnbalancedCollections.Any(c => c.Title == "Veille") && r.TagHygiene.SingleUseTags.Contains("dotnet")),
            Arg.Any<CancellationToken>());
    }

    private sealed class JobFixture
    {
        public ILibraryItemRepository LibraryItemRepository { get; } = Substitute.For<ILibraryItemRepository>();
        public IArticleRepository ArticleRepository { get; } = Substitute.For<IArticleRepository>();
        public IEmailExtractionLogRepository EmailExtractionLogRepository { get; } = Substitute.For<IEmailExtractionLogRepository>();
        public IWatchEvaluationLogRepository WatchEvaluationLogRepository { get; } = Substitute.For<IWatchEvaluationLogRepository>();
        public IRaindropClient RaindropClient { get; } = Substitute.For<IRaindropClient>();
        public IWeeklyDigestNotifier WeeklyDigestNotifier { get; } = Substitute.For<IWeeklyDigestNotifier>();

        public JobFixture()
        {
            LibraryItemRepository.GetAllForInsightsAsync(Arg.Any<CancellationToken>()).Returns([]);
            RaindropClient.GetTaxonomyAsync(Arg.Any<CancellationToken>()).Returns(EmptyTaxonomy);
            ArticleRepository.GetClassificationRawResponsesSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);
            ArticleRepository.GetTrackedArticlesAsync(Arg.Any<CancellationToken>()).Returns([]);
            EmailExtractionLogRepository.GetRawResponsesSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);
            WatchEvaluationLogRepository.GetRawResponsesSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);
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
            WatchEvaluationLogRepository,
            RaindropClient,
            WeeklyDigestNotifier,
            NullLogger<WeeklyInsightsJob>.Instance);
    }
}
