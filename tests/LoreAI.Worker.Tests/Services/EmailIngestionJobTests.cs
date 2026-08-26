using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using LoreAI.Core.Enums;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;
using LoreAI.Worker.Options;
using LoreAI.Worker.Services;

using MsOptions = Microsoft.Extensions.Options.Options;

namespace LoreAI.Worker.Tests.Services;

/// <summary>
/// Couvre les garanties propres à ce job, distinctes d'<c>UnsortedClassificationJob</c> (lot 8, #49) :
/// pas de write-back Raindrop, pas de journal de cycle, et surtout un repli/échec sur un lien n'interrompt
/// pas le lot — le curseur historyId est déjà avancé par IGmailIngester, indépendamment du sort de chaque item.
/// </summary>
public class EmailIngestionJobTests
{
    private static readonly RaindropTaxonomy EmptyTaxonomy = new([], []);

    [Fact]
    public async Task Invoke_NoNewItems_DoesNotLearnTaxonomyNorClassify()
    {
        var fixture = new JobFixture().WithNewItems();

        await fixture.Build().Invoke();

        await fixture.RaindropClient.DidNotReceive().GetTaxonomyAsync(Arg.Any<CancellationToken>());
        await fixture.Classifier.DidNotReceive().ClassifyAsync(
            Arg.Any<Item>(), Arg.Any<RaindropTaxonomy>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_NewItems_ClassifiesAndPersistsEachOne()
    {
        var fixture = new JobFixture()
            .WithNewItems(CreateItem("msg1:0"), CreateItem("msg1:1"))
            .WithClassification(CreateClassification());

        await fixture.Build().Invoke();

        await fixture.Classifier.Received(2).ClassifyAsync(
            Arg.Any<Item>(), Arg.Any<RaindropTaxonomy>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await fixture.ArticleRepository.Received(2).UpsertAsync(
            Arg.Any<Item>(), Arg.Any<ClassificationResult>(), Arg.Any<ContentFetchResult>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Jamais de write-back Raindrop pour une source Newsletter (ADR 0012).</summary>
    [Fact]
    public async Task Invoke_NewItem_NeverCallsRaindropWriteBack()
    {
        var fixture = new JobFixture()
            .WithNewItems(CreateItem("msg1:0"))
            .WithClassification(CreateClassification());

        await fixture.Build().Invoke();

        await fixture.RaindropClient.DidNotReceive().UpdateRaindropAsync(
            Arg.Any<long>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Contrairement à UnsortedClassificationJob : un repli sur un lien ne doit pas empêcher le traitement
    /// des liens suivants, le curseur Gmail étant déjà avancé indépendamment de chaque classification.
    /// </summary>
    [Fact]
    public async Task Invoke_FallbackOnFirstItem_StillProcessesSecondItem()
    {
        var fixture = new JobFixture()
            .WithNewItems(CreateItem("msg1:0"), CreateItem("msg1:1"))
            .WithClassificationSequence(
                ClassificationResult.Fallback("model", "boom", "{}"),
                CreateClassification());

        await fixture.Build().Invoke();

        await fixture.ArticleRepository.Received(2).UpsertAsync(
            Arg.Any<Item>(), Arg.Any<ClassificationResult>(), Arg.Any<ContentFetchResult>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Même logique qu'un repli : une exception sur un lien ne doit pas bloquer les suivants.</summary>
    [Fact]
    public async Task Invoke_ExceptionOnFirstItem_StillProcessesSecondItem()
    {
        var fixture = new JobFixture()
            .WithNewItems(CreateItem("msg1:0"), CreateItem("msg1:1"))
            .WithClassification(CreateClassification());
        fixture.ArticleRepository
            .UpsertAsync(Arg.Is<Item>(i => i!.SourceId == "msg1:0"), Arg.Any<ClassificationResult>(), Arg.Any<ContentFetchResult>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database is locked"));

        var exception = await Record.ExceptionAsync(() => fixture.Build().Invoke());

        Assert.Null(exception);
        await fixture.ArticleRepository.Received(1).UpsertAsync(
            Arg.Is<Item>(i => i!.SourceId == "msg1:1"), Arg.Any<ClassificationResult>(), Arg.Any<ContentFetchResult>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_GmailIngesterThrows_DoesNotThrow()
    {
        var fixture = new JobFixture();
        fixture.GmailIngester
            .GetNewItemsAsync(Arg.Any<PollingState>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("401 Unauthorized"));

        var exception = await Record.ExceptionAsync(() => fixture.Build().Invoke());

        Assert.Null(exception);
    }

    [Fact]
    public async Task Invoke_PolicyTriggers_NotifiesImmediately()
    {
        var fixture = new JobFixture()
            .WithNewItems(CreateItem("msg1:0"))
            .WithClassification(CreateClassification());
        fixture.NotificationPolicy.ShouldNotifyImmediately(Arg.Any<ClassificationResult>()).Returns(true);

        await fixture.Build().Invoke();

        await fixture.ImmediateNotifier.Received(1).NotifyAsync(
            Arg.Any<Item>(), Arg.Any<ClassificationResult>(), Arg.Any<CancellationToken>());
    }

    private static Item CreateItem(string sourceId) => new(
        SourceType.Newsletter,
        sourceId,
        $"https://blog.example.com/{sourceId}",
        $"Article {sourceId}",
        null,
        null,
        [],
        DateTimeOffset.UtcNow);

    private static ClassificationResult CreateClassification() =>
        new(null, [], RecommendedAction.ALire, Priority.Moyenne, "raison", "résumé", "model", "{}");

    private sealed class JobFixture
    {
        public IGmailIngester GmailIngester { get; } = Substitute.For<IGmailIngester>();
        public IPollingStateRepository PollingStateRepository { get; } = Substitute.For<IPollingStateRepository>();
        public IRaindropClient RaindropClient { get; } = Substitute.For<IRaindropClient>();
        public IArticleRepository ArticleRepository { get; } = Substitute.For<IArticleRepository>();
        public IClassifier Classifier { get; } = Substitute.For<IClassifier>();
        public IContentFetcher ContentFetcher { get; } = Substitute.For<IContentFetcher>();
        public IImmediateNotifier ImmediateNotifier { get; } = Substitute.For<IImmediateNotifier>();
        public INotificationPolicy NotificationPolicy { get; } = Substitute.For<INotificationPolicy>();
        public IToolRepository ToolRepository { get; } = Substitute.For<IToolRepository>();

        public JobFixture()
        {
            PollingStateRepository.GetAsync(Arg.Any<SourceType>(), Arg.Any<CancellationToken>()).Returns(PollingState.Initial(SourceType.Newsletter));
            GmailIngester.GetNewItemsAsync(Arg.Any<PollingState>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<Item>());
            RaindropClient.GetTaxonomyAsync(Arg.Any<CancellationToken>()).Returns(EmptyTaxonomy);
            NotificationPolicy.ShouldNotifyImmediately(Arg.Any<ClassificationResult>()).Returns(false);
            ContentFetcher.FetchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ContentFetchResult.Skipped);
            ArticleRepository
                .UpsertAsync(Arg.Any<Item>(), Arg.Any<ClassificationResult>(), Arg.Any<ContentFetchResult>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(1L);
        }

        public JobFixture WithNewItems(params Item[] items)
        {
            GmailIngester.GetNewItemsAsync(Arg.Any<PollingState>(), Arg.Any<CancellationToken>()).Returns(items);
            return this;
        }

        public JobFixture WithClassification(ClassificationResult result)
        {
            Classifier.ClassifyAsync(Arg.Any<Item>(), Arg.Any<RaindropTaxonomy>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(result);
            return this;
        }

        public JobFixture WithClassificationSequence(params ClassificationResult[] results)
        {
            Classifier.ClassifyAsync(Arg.Any<Item>(), Arg.Any<RaindropTaxonomy>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(results[0], results[1..]);
            return this;
        }

        public EmailIngestionJob Build()
        {
            var options = MsOptions.Create(new WorkerOptions());
            var classificationStep = new ArticleClassificationStep(
                Classifier,
                ContentFetcher,
                ArticleRepository,
                ImmediateNotifier,
                NotificationPolicy,
                ToolRepository,
                options,
                NullLogger<ArticleClassificationStep>.Instance);

            return new EmailIngestionJob(
                GmailIngester,
                PollingStateRepository,
                RaindropClient,
                classificationStep,
                NullLogger<EmailIngestionJob>.Instance);
        }
    }
}
