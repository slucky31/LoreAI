using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using LoreAI.Core.Enums;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;
using LoreAI.Worker.Services;

namespace LoreAI.Worker.Tests.Services;

/// <summary>Couvre L3 (réconciliation) et L4 (relance), greffée dans la même passe — lot 6, #47.</summary>
public class ReconciliationJobTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task Invoke_ItemDeletedFromRaindrop_RecordsDeletedLinkStatus()
    {
        var fixture = new JobFixture().WithCandidates(CreateCandidate(1));
        fixture.RaindropClient.GetRaindropAsync(1, Arg.Any<CancellationToken>()).Returns((RaindropSnapshot?)null);

        await fixture.Build().Invoke();

        await fixture.ArticleRepository.Received(1).RecordReconciliationAsync(
            1, Arg.Any<DateTimeOffset>(), null, null, LinkStatus.Deleted, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_ItemMarkedBrokenByRaindrop_RecordsBrokenLinkStatus()
    {
        var fixture = new JobFixture().WithCandidates(CreateCandidate(1));
        fixture.RaindropClient.GetRaindropAsync(1, Arg.Any<CancellationToken>())
            .Returns(new RaindropSnapshot(1, -1, ["dotnet"], Broken: true));

        await fixture.Build().Invoke();

        await fixture.ArticleRepository.Received(1).RecordReconciliationAsync(
            1, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset?>(), LinkStatus.Broken, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_TagsDivergeFromWhatWasWritten_MarksHumanHandled()
    {
        var candidate = CreateCandidate(1, originalTags: ["dotnet"], suggestedTags: ["ia"]);
        var fixture = new JobFixture().WithCandidates(candidate);
        // L'utilisateur a retiré "ia" et ajouté "perso" : divergence par rapport à dotnet+ia écrit par LoreAI.
        fixture.RaindropClient.GetRaindropAsync(1, Arg.Any<CancellationToken>())
            .Returns(new RaindropSnapshot(1, -1, ["dotnet", "perso"], Broken: false));

        await fixture.Build().Invoke();

        await fixture.ArticleRepository.Received(1).RecordReconciliationAsync(
            1, Arg.Any<DateTimeOffset>(), Arg.Is<DateTimeOffset?>(d => d != null), Arg.Any<DateTimeOffset?>(), LinkStatus.Ok, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_CollectionDivergesFromWriteBack_MarksHumanHandled()
    {
        var candidate = CreateCandidate(1, writeBackCollectionId: 10);
        var fixture = new JobFixture().WithCandidates(candidate);
        // L'article a été déplacé manuellement vers une autre collection que celle écrite par LoreAI.
        fixture.RaindropClient.GetRaindropAsync(1, Arg.Any<CancellationToken>())
            .Returns(new RaindropSnapshot(1, 99, candidate.OriginalTags.Concat(candidate.SuggestedTags).ToList(), Broken: false));

        await fixture.Build().Invoke();

        await fixture.ArticleRepository.Received(1).RecordReconciliationAsync(
            1, Arg.Any<DateTimeOffset>(), Arg.Is<DateTimeOffset?>(d => d != null), Arg.Any<DateTimeOffset?>(), LinkStatus.Ok, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_NothingChanged_LeavesHumanHandledNull()
    {
        var candidate = CreateCandidate(1, originalTags: ["dotnet"], suggestedTags: [], writeBackCollectionId: null);
        var fixture = new JobFixture().WithCandidates(candidate);
        fixture.RaindropClient.GetRaindropAsync(1, Arg.Any<CancellationToken>())
            .Returns(new RaindropSnapshot(1, -1, ["dotnet"], Broken: false));

        await fixture.Build().Invoke();

        await fixture.ArticleRepository.Received(1).RecordReconciliationAsync(
            1, Arg.Any<DateTimeOffset>(), null, null, LinkStatus.Ok, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_AlreadyHumanHandled_NeverReEvaluatesDivergence()
    {
        var candidate = CreateCandidate(1, humanHandledAtUtc: Now.AddDays(-5));
        var fixture = new JobFixture().WithCandidates(candidate);
        // Peu importe ce qui a encore changé depuis : HumanHandledAtUtc ne doit jamais être ré-évalué une fois posé.
        fixture.RaindropClient.GetRaindropAsync(1, Arg.Any<CancellationToken>())
            .Returns(new RaindropSnapshot(1, 12345, ["autre-chose"], Broken: false));

        await fixture.Build().Invoke();

        await fixture.ArticleRepository.Received(1).RecordReconciliationAsync(
            1, Arg.Any<DateTimeOffset>(), candidate.HumanHandledAtUtc, Arg.Any<DateTimeOffset?>(), LinkStatus.Ok, Arg.Any<CancellationToken>());
    }

    // --- L4 : relance -----------------------------------------------------------------------------

    [Fact]
    public async Task Invoke_ATesterHauteUnhandledPast14Days_SendsReminderOnce()
    {
        var candidate = CreateCandidate(1, action: RecommendedAction.ATester, priority: Priority.Haute, classifiedAtUtc: Now.AddDays(-15));
        var fixture = new JobFixture().WithCandidates(candidate);
        fixture.RaindropClient.GetRaindropAsync(1, Arg.Any<CancellationToken>())
            .Returns(new RaindropSnapshot(1, -1, [], Broken: false));

        await fixture.Build().Invoke();

        await fixture.ReminderNotifier.Received(1).NotifyAsync(candidate.Title, candidate.Url, Arg.Any<int>(), Arg.Any<CancellationToken>());
        await fixture.ArticleRepository.Received(1).RecordReconciliationAsync(
            1, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset?>(), Arg.Is<DateTimeOffset?>(d => d != null), Arg.Any<LinkStatus>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_AlreadyReminded_NeverSendsTwice()
    {
        var candidate = CreateCandidate(1, action: RecommendedAction.ATester, priority: Priority.Haute, classifiedAtUtc: Now.AddDays(-30), remindedAtUtc: Now.AddDays(-16));
        var fixture = new JobFixture().WithCandidates(candidate);
        fixture.RaindropClient.GetRaindropAsync(1, Arg.Any<CancellationToken>())
            .Returns(new RaindropSnapshot(1, -1, [], Broken: false));

        await fixture.Build().Invoke();

        await fixture.ReminderNotifier.DidNotReceive().NotifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_ATesterHauteUnder14Days_DoesNotRemindYet()
    {
        var candidate = CreateCandidate(1, action: RecommendedAction.ATester, priority: Priority.Haute, classifiedAtUtc: Now.AddDays(-3));
        var fixture = new JobFixture().WithCandidates(candidate);
        fixture.RaindropClient.GetRaindropAsync(1, Arg.Any<CancellationToken>())
            .Returns(new RaindropSnapshot(1, -1, [], Broken: false));

        await fixture.Build().Invoke();

        await fixture.ReminderNotifier.DidNotReceive().NotifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_LowerPriorityUnhandled_NeverReminds()
    {
        var candidate = CreateCandidate(1, action: RecommendedAction.ATester, priority: Priority.Basse, classifiedAtUtc: Now.AddDays(-30));
        var fixture = new JobFixture().WithCandidates(candidate);
        fixture.RaindropClient.GetRaindropAsync(1, Arg.Any<CancellationToken>())
            .Returns(new RaindropSnapshot(1, -1, [], Broken: false));

        await fixture.Build().Invoke();

        await fixture.ReminderNotifier.DidNotReceive().NotifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_FailureOnOneArticle_StillProcessesTheRest()
    {
        var fixture = new JobFixture().WithCandidates(CreateCandidate(1), CreateCandidate(2));
        fixture.RaindropClient.GetRaindropAsync(1, Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("502"));
        fixture.RaindropClient.GetRaindropAsync(2, Arg.Any<CancellationToken>()).Returns(new RaindropSnapshot(2, -1, [], Broken: false));

        var exception = await Record.ExceptionAsync(() => fixture.Build().Invoke());

        Assert.Null(exception);
        await fixture.ArticleRepository.Received(1).RecordReconciliationAsync(
            2, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<LinkStatus>(), Arg.Any<CancellationToken>());
        await fixture.ArticleRepository.DidNotReceive().RecordReconciliationAsync(
            1, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<LinkStatus>(), Arg.Any<CancellationToken>());
    }

    private static ReconciliationCandidate CreateCandidate(
        long id,
        IReadOnlyList<string>? originalTags = null,
        IReadOnlyList<string>? suggestedTags = null,
        long? writeBackCollectionId = null,
        RecommendedAction action = RecommendedAction.ALire,
        Priority priority = Priority.Basse,
        DateTimeOffset? classifiedAtUtc = null,
        DateTimeOffset? humanHandledAtUtc = null,
        DateTimeOffset? remindedAtUtc = null) =>
        new(id, $"Titre {id}", $"https://example.com/{id}",
            originalTags ?? [], suggestedTags ?? [], writeBackCollectionId,
            action, priority, classifiedAtUtc ?? Now.AddDays(-1), humanHandledAtUtc, remindedAtUtc);

    private sealed class JobFixture
    {
        public IRaindropClient RaindropClient { get; } = Substitute.For<IRaindropClient>();
        public IArticleRepository ArticleRepository { get; } = Substitute.For<IArticleRepository>();
        public IReminderNotifier ReminderNotifier { get; } = Substitute.For<IReminderNotifier>();

        public JobFixture()
        {
            ArticleRepository.GetReconciliationCandidatesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        }

        public JobFixture WithCandidates(params ReconciliationCandidate[] candidates)
        {
            ArticleRepository.GetReconciliationCandidatesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(candidates);
            return this;
        }

        public ReconciliationJob Build() => new(RaindropClient, ArticleRepository, ReminderNotifier, NullLogger<ReconciliationJob>.Instance);
    }
}
