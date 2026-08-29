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
/// Couvre L5 (lot 8) : pose/retrait du tag « cette-semaine », jamais de déplacement de collection ni de
/// réécriture de note, articles non-Raindrop de la file jamais interrogés, un échec sur un article
/// n'interrompt pas les autres.
/// </summary>
public class ReadingQueueTaggingJobTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task Invoke_NewEntryInQueue_TagsItAndPreservesNoteAndCollection()
    {
        var article = CreateTrackedArticle(1, sourceId: "10");
        var fixture = new JobFixture().WithTrackedArticles(article);
        fixture.RaindropClient.GetRaindropAsync(10, Arg.Any<CancellationToken>())
            .Returns(new RaindropSnapshot(10, 55, ["dotnet"], Broken: false, Note: "note existante"));

        await fixture.Build().Invoke();

        await fixture.RaindropClient.Received(1).UpdateRaindropAsync(
            10, Arg.Is<IReadOnlyCollection<string>>(t => t!.Contains("dotnet") && t!.Contains("cette-semaine")), "note existante", null, Arg.Any<CancellationToken>());
        await fixture.ArticleRepository.Received(1).SetReadingQueueTagAsync(1, Arg.Is<DateTimeOffset?>(d => d != null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_AlreadyTaggedManuallyOnRaindrop_DoesNotDuplicateTagButStillRecordsTracking()
    {
        var article = CreateTrackedArticle(1, sourceId: "10");
        var fixture = new JobFixture().WithTrackedArticles(article);
        fixture.RaindropClient.GetRaindropAsync(10, Arg.Any<CancellationToken>())
            .Returns(new RaindropSnapshot(10, -1, ["cette-semaine"], Broken: false));

        await fixture.Build().Invoke();

        await fixture.RaindropClient.DidNotReceive().UpdateRaindropAsync(
            Arg.Any<long>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>());
        await fixture.ArticleRepository.Received(1).SetReadingQueueTagAsync(1, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_ArticleLeftTheQueue_UntagsIt()
    {
        var fixture = new JobFixture()
            .WithTrackedArticles() // file vide désormais
            .WithCurrentlyTagged(new ReadingQueueTaggedArticle(1, "10"));
        fixture.RaindropClient.GetRaindropAsync(10, Arg.Any<CancellationToken>())
            .Returns(new RaindropSnapshot(10, -1, ["dotnet", "cette-semaine"], Broken: false, Note: "note"));

        await fixture.Build().Invoke();

        await fixture.RaindropClient.Received(1).UpdateRaindropAsync(
            10, Arg.Is<IReadOnlyCollection<string>>(t => t!.Contains("dotnet") && !t!.Contains("cette-semaine")), "note", null, Arg.Any<CancellationToken>());
        await fixture.ArticleRepository.Received(1).SetReadingQueueTagAsync(1, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_ArticleStillInQueue_NeverUntagged()
    {
        var article = CreateTrackedArticle(1, sourceId: "10");
        var fixture = new JobFixture()
            .WithTrackedArticles(article)
            .WithCurrentlyTagged(new ReadingQueueTaggedArticle(1, "10"));

        await fixture.Build().Invoke();

        await fixture.RaindropClient.DidNotReceive().GetRaindropAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
        await fixture.ArticleRepository.DidNotReceive().SetReadingQueueTagAsync(Arg.Any<long>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_ItemDeletedFromRaindropWhileUntagging_ClearsTrackingWithoutError()
    {
        var fixture = new JobFixture()
            .WithTrackedArticles()
            .WithCurrentlyTagged(new ReadingQueueTaggedArticle(1, "10"));
        fixture.RaindropClient.GetRaindropAsync(10, Arg.Any<CancellationToken>()).Returns((RaindropSnapshot?)null);

        var exception = await Record.ExceptionAsync(() => fixture.Build().Invoke());

        Assert.Null(exception);
        await fixture.RaindropClient.DidNotReceive().UpdateRaindropAsync(
            Arg.Any<long>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>());
        await fixture.ArticleRepository.Received(1).SetReadingQueueTagAsync(1, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_NonRaindropSourceInQueue_NeverQueriesRaindrop()
    {
        var newsletterArticle = CreateTrackedArticle(1, sourceId: "abc:0", sourceType: SourceType.Newsletter);
        var fixture = new JobFixture().WithTrackedArticles(newsletterArticle);

        await fixture.Build().Invoke();

        await fixture.RaindropClient.DidNotReceive().GetRaindropAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_FailureOnOneArticle_StillProcessesTheRest()
    {
        var fixture = new JobFixture().WithTrackedArticles(
            CreateTrackedArticle(1, sourceId: "10", priority: Priority.Haute),
            CreateTrackedArticle(2, sourceId: "20", priority: Priority.Basse));
        fixture.RaindropClient.GetRaindropAsync(10, Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("502"));
        fixture.RaindropClient.GetRaindropAsync(20, Arg.Any<CancellationToken>()).Returns(new RaindropSnapshot(20, -1, [], Broken: false));

        var exception = await Record.ExceptionAsync(() => fixture.Build().Invoke());

        Assert.Null(exception);
        await fixture.ArticleRepository.Received(1).SetReadingQueueTagAsync(2, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
        await fixture.ArticleRepository.DidNotReceive().SetReadingQueueTagAsync(1, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }

    private static TrackedArticle CreateTrackedArticle(
        long id,
        string sourceId,
        SourceType sourceType = SourceType.Raindrop,
        Priority priority = Priority.Haute) =>
        new(id, $"Titre {id}", $"https://example.com/{id}", RecommendedAction.ALire, priority,
            Now.AddDays(-1), Now.AddDays(-1), null, null, LinkStatus.Ok, sourceType, sourceId);

    private sealed class JobFixture
    {
        public IArticleRepository ArticleRepository { get; } = Substitute.For<IArticleRepository>();
        public IRaindropClient RaindropClient { get; } = Substitute.For<IRaindropClient>();

        public JobFixture()
        {
            ArticleRepository.GetTrackedArticlesAsync(Arg.Any<CancellationToken>()).Returns([]);
            ArticleRepository.GetReadingQueueTaggedAsync(Arg.Any<CancellationToken>()).Returns([]);
        }

        public JobFixture WithTrackedArticles(params TrackedArticle[] articles)
        {
            ArticleRepository.GetTrackedArticlesAsync(Arg.Any<CancellationToken>()).Returns(articles);
            return this;
        }

        public JobFixture WithCurrentlyTagged(params ReadingQueueTaggedArticle[] tagged)
        {
            ArticleRepository.GetReadingQueueTaggedAsync(Arg.Any<CancellationToken>()).Returns(tagged);
            return this;
        }

        public ReadingQueueTaggingJob Build() =>
            new(ArticleRepository, RaindropClient, MsOptions.Create(new WorkerOptions()), NullLogger<ReadingQueueTaggingJob>.Instance);
    }
}
