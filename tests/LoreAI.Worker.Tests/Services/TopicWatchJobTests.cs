using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using LoreAI.Core.Enums;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;
using LoreAI.Worker.Services;

namespace LoreAI.Worker.Tests.Services;

/// <summary>
/// Couvre l'orchestration de la veille (lot 9, #50, redesign) : boucle multi-sujets, création directe dans
/// Raindrop (pas de notif par article), curseur par sujet, digest groupé en une seule notification.
/// </summary>
public class TopicWatchJobTests
{
    private static readonly RaindropTaxonomy EmptyTaxonomy = new([], []);

    [Fact]
    public async Task Invoke_NoTopics_DoesNotReadCategoriesNorSendDigest()
    {
        var fixture = new JobFixture();

        await fixture.Build().Invoke();

        await fixture.MinifluxCategoryReader.DidNotReceive().GetNewEntriesAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await fixture.WatchDigestNotifier.DidNotReceive().NotifyAsync(Arg.Any<WatchRunSummary>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_TopicWithoutCursor_SkipsWithoutReadingCategory()
    {
        var fixture = new JobFixture().WithTopics(CreateTopic(1, "sujet", lastEntryId: null));

        await fixture.Build().Invoke();

        await fixture.MinifluxCategoryReader.DidNotReceive().GetNewEntriesAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_RelevantAndNewCandidate_CreatesRaindropWithWatchTagAndProposedTags()
    {
        var topic = CreateTopic(1, "dotnet-perf", "0", collectionId: 42);
        var fixture = new JobFixture()
            .WithTopics(topic)
            .WithEntries(topic.MinifluxCategoryId, CreateCandidate("101"))
            .WithEvaluation(CreateEvaluation(isRelevant: true, isNew: true, tags: ["dotnet"]));

        await fixture.Build().Invoke();

        await fixture.RaindropClient.Received(1).CreateRaindropAsync(
            "https://blog.example.com/101", "Article 101", 42,
            Arg.Is<IReadOnlyCollection<string>>(tags => tags!.Contains("dotnet") && tags!.Contains("veille")),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_RelevantButNotNewCandidate_DoesNotCreateRaindrop()
    {
        var topic = CreateTopic(1, "sujet", "0");
        var fixture = new JobFixture()
            .WithTopics(topic)
            .WithEntries(topic.MinifluxCategoryId, CreateCandidate("101"))
            .WithEvaluation(CreateEvaluation(isRelevant: true, isNew: false));

        await fixture.Build().Invoke();

        await fixture.RaindropClient.DidNotReceive().CreateRaindropAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_FallbackEvaluation_DoesNotCreateButStillRecordsUsageAndAdvancesCursor()
    {
        var topic = CreateTopic(1, "sujet", "0");
        var fixture = new JobFixture()
            .WithTopics(topic)
            .WithEntries(topic.MinifluxCategoryId, CreateCandidate("101"), lastEntryId: "101")
            .WithEvaluationResult(WatchEvaluation.Fallback("model", "boom", "{}"));

        await fixture.Build().Invoke();

        await fixture.RaindropClient.DidNotReceive().CreateRaindropAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await fixture.WatchEvaluationLogRepository.Received(1).RecordAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await fixture.WatchTopicRepository.Received(1).UpdateCursorAsync(1, "101", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_MultipleTopics_ProcessesEachIndependentlyAndSendsOneGroupedDigest()
    {
        var topicA = CreateTopic(1, "dotnet-perf", "0", categoryId: 10, collectionId: 100);
        var topicB = CreateTopic(2, "ia-outils", "0", categoryId: 20, collectionId: 200);
        var fixture = new JobFixture()
            .WithTopics(topicA, topicB)
            .WithEntries(10, CreateCandidate("101"))
            .WithEntries(20, CreateCandidate("201"))
            .WithEvaluation(CreateEvaluation(isRelevant: true, isNew: true));

        await fixture.Build().Invoke();

        await fixture.WatchDigestNotifier.Received(1).NotifyAsync(
            Arg.Is<WatchRunSummary>(s => s!.Topics.Count == 2
                && s.Topics.Any(t => t.TopicName == "dotnet-perf" && t.AddedCount == 1)
                && s.Topics.Any(t => t.TopicName == "ia-outils" && t.AddedCount == 1)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_ExceptionOnFirstTopic_StillProcessesSecondTopic()
    {
        var topicA = CreateTopic(1, "sujet-a", "0", categoryId: 10, collectionId: 100);
        var topicB = CreateTopic(2, "sujet-b", "0", categoryId: 20, collectionId: 200);
        var fixture = new JobFixture()
            .WithTopics(topicA, topicB)
            .WithEntries(20, CreateCandidate("201"))
            .WithEvaluation(CreateEvaluation(isRelevant: true, isNew: true));
        fixture.MinifluxCategoryReader.GetNewEntriesAsync(10, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("boom"));

        var exception = await Record.ExceptionAsync(() => fixture.Build().Invoke());

        Assert.Null(exception);
        await fixture.RaindropClient.Received(1).CreateRaindropAsync(
            "https://blog.example.com/201", Arg.Any<string>(), 200, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_ExceptionOnFirstCandidate_StillProcessesSecondCandidate()
    {
        var topic = CreateTopic(1, "sujet", "0");
        var fixture = new JobFixture()
            .WithTopics(topic)
            .WithEntries(topic.MinifluxCategoryId, [CreateCandidate("101"), CreateCandidate("102")])
            .WithEvaluation(CreateEvaluation(isRelevant: true, isNew: true));
        fixture.CorpusQueryRepository
            .SearchAsync("Article 101", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database is locked"));

        var exception = await Record.ExceptionAsync(() => fixture.Build().Invoke());

        Assert.Null(exception);
        await fixture.RaindropClient.Received(1).CreateRaindropAsync(
            "https://blog.example.com/102", Arg.Any<string>(), Arg.Any<long>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_WatchTopicRepositoryThrows_DoesNotThrow()
    {
        var fixture = new JobFixture();
        fixture.WatchTopicRepository.GetAllAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("database is locked"));

        var exception = await Record.ExceptionAsync(() => fixture.Build().Invoke());

        Assert.Null(exception);
    }

    private static Item CreateCandidate(string sourceId) => new(
        SourceType.Watch,
        sourceId,
        $"https://blog.example.com/{sourceId}",
        $"Article {sourceId}",
        null,
        null,
        [],
        DateTimeOffset.UtcNow);

    private static WatchTopic CreateTopic(long id, string name, string? lastEntryId, int categoryId = 1, long collectionId = 1) =>
        new(id, name, "description", categoryId, collectionId, lastEntryId, DateTimeOffset.UtcNow);

    private static WatchEvaluation CreateEvaluation(bool isRelevant, bool isNew, IReadOnlyList<string>? tags = null) =>
        new(isRelevant, isNew, tags ?? [], "raison", "model", "{}");

    private sealed class JobFixture
    {
        public IWatchTopicRepository WatchTopicRepository { get; } = Substitute.For<IWatchTopicRepository>();
        public IMinifluxCategoryReader MinifluxCategoryReader { get; } = Substitute.For<IMinifluxCategoryReader>();
        public ICorpusQueryRepository CorpusQueryRepository { get; } = Substitute.For<ICorpusQueryRepository>();
        public ITopicWatchFilter TopicWatchFilter { get; } = Substitute.For<ITopicWatchFilter>();
        public IRaindropClient RaindropClient { get; } = Substitute.For<IRaindropClient>();
        public IWatchDigestNotifier WatchDigestNotifier { get; } = Substitute.For<IWatchDigestNotifier>();
        public IWatchEvaluationLogRepository WatchEvaluationLogRepository { get; } = Substitute.For<IWatchEvaluationLogRepository>();

        public JobFixture()
        {
            WatchTopicRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<WatchTopic>());
            RaindropClient.GetTaxonomyAsync(Arg.Any<CancellationToken>()).Returns(EmptyTaxonomy);
            CorpusQueryRepository.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<LibraryItemSummary>());
            MinifluxCategoryReader.GetNewEntriesAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns((Array.Empty<Item>(), (string?)null));
        }

        public JobFixture WithTopics(params WatchTopic[] topics)
        {
            WatchTopicRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(topics);
            return this;
        }

        public JobFixture WithEntries(int categoryId, Item item, string? lastEntryId = "999") =>
            WithEntries(categoryId, [item], lastEntryId);

        public JobFixture WithEntries(int categoryId, IReadOnlyList<Item> items, string? lastEntryId = "999")
        {
            MinifluxCategoryReader.GetNewEntriesAsync(categoryId, Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns((items, lastEntryId));
            return this;
        }

        public JobFixture WithEvaluation(WatchEvaluation result) => WithEvaluationResult(result);

        public JobFixture WithEvaluationResult(WatchEvaluation result)
        {
            TopicWatchFilter.EvaluateAsync(
                Arg.Any<Item>(), Arg.Any<WatchTopic>(), Arg.Any<RaindropTaxonomy>(), Arg.Any<IReadOnlyList<LibraryItemSummary>>(), Arg.Any<CancellationToken>())
                .Returns(result);
            return this;
        }

        public TopicWatchJob Build() => new(
            WatchTopicRepository,
            MinifluxCategoryReader,
            CorpusQueryRepository,
            TopicWatchFilter,
            RaindropClient,
            WatchDigestNotifier,
            WatchEvaluationLogRepository,
            NullLogger<TopicWatchJob>.Instance);
    }
}
