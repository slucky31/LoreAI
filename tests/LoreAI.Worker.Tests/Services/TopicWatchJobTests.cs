using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using LoreAI.Core.Enums;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;
using LoreAI.Infrastructure.Feed;
using LoreAI.Worker.Services;

using MsOptions = Microsoft.Extensions.Options.Options;

namespace LoreAI.Worker.Tests.Services;

/// <summary>
/// Couvre l'orchestration de la veille (lot 9, #50) : jamais de write-back, jamais d'<see cref="Item"/>
/// persisté, un repli ou une exception sur une entrée n'interrompt pas le lot (curseur déjà avancé par
/// l'ingesteur, même raisonnement que <c>FeedIngestionJobTests</c>).
/// </summary>
public class TopicWatchJobTests
{
    [Fact]
    public async Task Invoke_NoNewCandidates_DoesNotSearchCorpusNorEvaluate()
    {
        var fixture = new JobFixture();

        await fixture.Build().Invoke();

        await fixture.CorpusQueryRepository.DidNotReceive().SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await fixture.TopicWatchFilter.DidNotReceive().EvaluateAsync(
            Arg.Any<Item>(), Arg.Any<IReadOnlyList<WatchTopic>>(), Arg.Any<IReadOnlyList<LibraryItemSummary>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_RelevantAndNewCandidate_NotifiesAndRecordsUsage()
    {
        var fixture = new JobFixture()
            .WithCandidates(CreateCandidate("101"))
            .WithEvaluation(CreateEvaluation(isRelevant: true, isNew: true));

        await fixture.Build().Invoke();

        await fixture.TopicWatchNotifier.Received(1).NotifyAsync(Arg.Any<Item>(), Arg.Any<WatchEvaluation>(), Arg.Any<CancellationToken>());
        await fixture.WatchEvaluationLogRepository.Received(1).RecordAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_RelevantButNotNewCandidate_DoesNotNotify()
    {
        var fixture = new JobFixture()
            .WithCandidates(CreateCandidate("101"))
            .WithEvaluation(CreateEvaluation(isRelevant: true, isNew: false));

        await fixture.Build().Invoke();

        await fixture.TopicWatchNotifier.DidNotReceive().NotifyAsync(Arg.Any<Item>(), Arg.Any<WatchEvaluation>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_NotRelevantCandidate_DoesNotNotify()
    {
        var fixture = new JobFixture()
            .WithCandidates(CreateCandidate("101"))
            .WithEvaluation(CreateEvaluation(isRelevant: false, isNew: false));

        await fixture.Build().Invoke();

        await fixture.TopicWatchNotifier.DidNotReceive().NotifyAsync(Arg.Any<Item>(), Arg.Any<WatchEvaluation>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_FallbackEvaluation_DoesNotNotifyButStillRecordsUsage()
    {
        var fixture = new JobFixture()
            .WithCandidates(CreateCandidate("101"))
            .WithEvaluationResult(WatchEvaluation.Fallback("model", "boom", "{}"));

        await fixture.Build().Invoke();

        await fixture.TopicWatchNotifier.DidNotReceive().NotifyAsync(Arg.Any<Item>(), Arg.Any<WatchEvaluation>(), Arg.Any<CancellationToken>());
        await fixture.WatchEvaluationLogRepository.Received(1).RecordAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_ExceptionOnFirstCandidate_StillProcessesSecondCandidate()
    {
        var fixture = new JobFixture()
            .WithCandidates(CreateCandidate("101"), CreateCandidate("102"))
            .WithEvaluation(CreateEvaluation(isRelevant: true, isNew: true));
        fixture.CorpusQueryRepository
            .SearchAsync(Arg.Is<string>(t => t == "Article 101"), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database is locked"));

        var exception = await Record.ExceptionAsync(() => fixture.Build().Invoke());

        Assert.Null(exception);
        await fixture.TopicWatchNotifier.Received(1).NotifyAsync(
            Arg.Is<Item>(i => i!.SourceId == "102"), Arg.Any<WatchEvaluation>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_WatchIngesterThrows_DoesNotThrow()
    {
        var fixture = new JobFixture();
        fixture.WatchIngester.GetNewItemsAsync(Arg.Any<PollingState>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("401 Unauthorized"));

        var exception = await Record.ExceptionAsync(() => fixture.Build().Invoke());

        Assert.Null(exception);
    }

    [Fact]
    public async Task Invoke_LogRepositoryThrows_StillNotifies()
    {
        var fixture = new JobFixture()
            .WithCandidates(CreateCandidate("101"))
            .WithEvaluation(CreateEvaluation(isRelevant: true, isNew: true));
        fixture.WatchEvaluationLogRepository
            .RecordAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database is locked"));

        var exception = await Record.ExceptionAsync(() => fixture.Build().Invoke());

        Assert.Null(exception);
        await fixture.TopicWatchNotifier.Received(1).NotifyAsync(Arg.Any<Item>(), Arg.Any<WatchEvaluation>(), Arg.Any<CancellationToken>());
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

    private static WatchEvaluation CreateEvaluation(bool isRelevant, bool isNew) =>
        new(isRelevant, isNew, isRelevant ? "sujet" : null, "raison", "model", "{}");

    private sealed class JobFixture
    {
        public ISourceIngester WatchIngester { get; } = Substitute.For<ISourceIngester>();
        public IPollingStateRepository PollingStateRepository { get; } = Substitute.For<IPollingStateRepository>();
        public ICorpusQueryRepository CorpusQueryRepository { get; } = Substitute.For<ICorpusQueryRepository>();
        public ITopicWatchFilter TopicWatchFilter { get; } = Substitute.For<ITopicWatchFilter>();
        public ITopicWatchNotifier TopicWatchNotifier { get; } = Substitute.For<ITopicWatchNotifier>();
        public IWatchEvaluationLogRepository WatchEvaluationLogRepository { get; } = Substitute.For<IWatchEvaluationLogRepository>();

        public JobFixture()
        {
            PollingStateRepository.GetAsync(Arg.Any<SourceType>(), Arg.Any<CancellationToken>()).Returns(PollingState.Initial(SourceType.Watch));
            WatchIngester.GetNewItemsAsync(Arg.Any<PollingState>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<Item>());
            CorpusQueryRepository.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<LibraryItemSummary>());
        }

        public JobFixture WithCandidates(params Item[] items)
        {
            WatchIngester.GetNewItemsAsync(Arg.Any<PollingState>(), Arg.Any<CancellationToken>()).Returns(items);
            return this;
        }

        public JobFixture WithEvaluation(WatchEvaluation result) => WithEvaluationResult(result);

        public JobFixture WithEvaluationResult(WatchEvaluation result)
        {
            TopicWatchFilter.EvaluateAsync(
                Arg.Any<Item>(), Arg.Any<IReadOnlyList<WatchTopic>>(), Arg.Any<IReadOnlyList<LibraryItemSummary>>(), Arg.Any<CancellationToken>())
                .Returns(result);
            return this;
        }

        public TopicWatchJob Build() => new(
            WatchIngester,
            PollingStateRepository,
            CorpusQueryRepository,
            TopicWatchFilter,
            TopicWatchNotifier,
            WatchEvaluationLogRepository,
            MsOptions.Create(new WatchOptions()),
            NullLogger<TopicWatchJob>.Instance);
    }
}
