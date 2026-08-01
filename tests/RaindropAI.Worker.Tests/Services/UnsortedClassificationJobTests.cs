using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RaindropAI.Core.Enums;
using RaindropAI.Core.Interfaces;
using RaindropAI.Core.Models;
using RaindropAI.Worker.Options;
using RaindropAI.Worker.Services;

// RaindropAI.Worker.Options masque Microsoft.Extensions.Options dans ce fichier.
using MsOptions = Microsoft.Extensions.Options.Options;

namespace RaindropAI.Worker.Tests.Services;

/// <summary>
/// Couvre l'orchestration du cycle de tri : c'est ici que vivent les garanties promises par le README
/// (tags jamais perdus, déplacement seulement sur correspondance exacte, rien n'est touché en mode « à blanc »)
/// et les invariants des findings F-01, F-03 et F-04.
/// </summary>
public class UnsortedClassificationJobTests
{
    private static readonly RaindropCollection DotNetCollection = new(10, ".NET");
    private static readonly RaindropTaxonomy Taxonomy = new([DotNetCollection], [new RaindropTag("dotnet", 12)]);

    // --- Garanties de write-back -------------------------------------------------------------

    [Fact]
    public async Task Invoke_MatchingCollection_MovesItemAndMergesTags()
    {
        var fixture = new JobFixture()
            .WithNewItems(CreateItem(1, tags: ["Perso"]))
            .WithClassification(CreateClassification(".NET", ["dotnet", "PERSO"]));

        await fixture.Build().Invoke();

        await fixture.RaindropClient.Received(1).UpdateRaindropAsync(
            1,
            Arg.Is<IReadOnlyCollection<string>>(t => t.Count == 2 && t.Contains("Perso") && t.Contains("dotnet")),
            Arg.Any<string>(),
            DotNetCollection.Id,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_UnknownCollectionTitle_AppliesTagsButDoesNotMove()
    {
        var fixture = new JobFixture()
            .WithNewItems(CreateItem(1))
            .WithClassification(CreateClassification("Collection inventée par le LLM", ["dotnet"]));

        await fixture.Build().Invoke();

        // Le titre suggéré ne correspond à aucune collection réelle : on ne déplace pas.
        await fixture.RaindropClient.Received(1).UpdateRaindropAsync(
            1, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<string>(), null, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// F-17 : deux collections peuvent porter le même titre sous des parents différents. Ranger au hasard
    /// serait pire que ne pas ranger — l'article garde ses tags et reste dans « Non trié ».
    /// </summary>
    [Fact]
    public async Task Invoke_AmbiguousCollectionTitle_AppliesTagsButDoesNotMove()
    {
        var ambiguousTaxonomy = new RaindropTaxonomy(
            [new RaindropCollection(10, "Veille"), new RaindropCollection(20, "Veille")],
            []);

        var fixture = new JobFixture()
            .WithTaxonomy(ambiguousTaxonomy)
            .WithNewItems(CreateItem(1))
            .WithClassification(CreateClassification("Veille", ["dotnet"]));

        await fixture.Build().Invoke();

        await fixture.RaindropClient.Received(1).UpdateRaindropAsync(
            1, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<string>(), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_TagMergeIsCaseInsensitiveAndNeverLosesExistingTags()
    {
        var fixture = new JobFixture()
            .WithNewItems(CreateItem(1, tags: ["DotNet", "veille"]))
            .WithClassification(CreateClassification(null, ["dotnet", "claude"]));

        await fixture.Build().Invoke();

        await fixture.RaindropClient.Received(1).UpdateRaindropAsync(
            1,
            Arg.Is<IReadOnlyCollection<string>>(t =>
                t.Count == 3 && t.Contains("DotNet") && t.Contains("veille") && t.Contains("claude")),
            Arg.Any<string>(),
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_WriteBackDisabled_ClassifiesAndPersistsWithoutTouchingRaindrop()
    {
        var fixture = new JobFixture()
            .WithWriteBack(false)
            .WithNewItems(CreateItem(1))
            .WithClassification(CreateClassification(".NET", ["dotnet"]));

        await fixture.Build().Invoke();

        await fixture.RaindropClient.DidNotReceive().UpdateRaindropAsync(
            Arg.Any<long>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>());
        await fixture.ArticleRepository.Received(1).UpsertAsync(
            Arg.Any<RaindropItem>(), Arg.Any<ClassificationResult>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Le bloc de note est reconstruit à partir de la note existante — cf. F-04.</summary>
    [Fact]
    public async Task Invoke_ReappliedOnAnAlreadyAnnotatedItem_DoesNotStackNoteBlocks()
    {
        var alreadyAnnotated = "Ma note perso.\n\n[RaindropAI] ALire — Basse — analyse précédente";
        var fixture = new JobFixture()
            .WithNewItems(CreateItem(1) with { Note = alreadyAnnotated })
            .WithClassification(CreateClassification(null, [], reason: "analyse à jour"));

        await fixture.Build().Invoke();

        await fixture.RaindropClient.Received(1).UpdateRaindropAsync(
            1,
            Arg.Any<IReadOnlyCollection<string>>(),
            "Ma note perso.\n\n[RaindropAI] ATester — Haute — analyse à jour",
            Arg.Any<long?>(),
            Arg.Any<CancellationToken>());
    }

    // --- F-01 : une classification en repli ne doit rien écrire ------------------------------

    [Fact]
    public async Task Invoke_FallbackClassification_DoesNotWriteToRaindrop()
    {
        var fixture = new JobFixture()
            .WithNewItems(CreateItem(1))
            .WithClassification(ClassificationResult.Fallback("model", "Classification échouée: 429", "{}"));

        await fixture.Build().Invoke();

        await fixture.RaindropClient.DidNotReceive().UpdateRaindropAsync(
            Arg.Any<long>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_FallbackClassification_IsStillPersistedForAudit()
    {
        var fixture = new JobFixture()
            .WithNewItems(CreateItem(1))
            .WithClassification(ClassificationResult.Fallback("model", "Classification échouée: 429", "{}"));

        await fixture.Build().Invoke();

        await fixture.ArticleRepository.Received(1).UpsertAsync(
            Arg.Any<RaindropItem>(), Arg.Is<ClassificationResult>(c => c.IsFallback), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_FallbackOnFirstItem_DoesNotAdvancePollingState()
    {
        var fixture = new JobFixture()
            .WithNewItems(CreateItem(1))
            .WithClassification(ClassificationResult.Fallback("model", "boom", "{}"));

        await fixture.Build().Invoke();

        // Sans cela l'article serait dépassé par le high-water mark et jamais reclassé.
        await fixture.PollingStateRepository.DidNotReceive().UpdateAsync(Arg.Any<PollingState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_FallbackOnSecondItem_KeepsProgressOnTheFirst()
    {
        var fixture = new JobFixture()
            .WithNewItems(CreateItem(1), CreateItem(2), CreateItem(3))
            .WithClassificationSequence(
                CreateClassification(".NET", ["dotnet"]),
                ClassificationResult.Fallback("model", "boom", "{}"),
                CreateClassification(".NET", ["dotnet"]));

        await fixture.Build().Invoke();

        await fixture.PollingStateRepository.Received(1).UpdateAsync(
            Arg.Is<PollingState>(s => s.LastRaindropId == 1), Arg.Any<CancellationToken>());
        // Le 3e n'est pas traité : reprendre au 2 est la seule façon de ne pas le perdre.
        await fixture.RaindropClient.DidNotReceive().UpdateRaindropAsync(
            3, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_FallbackClassification_DoesNotNotifyDiscord()
    {
        var fixture = new JobFixture()
            .WithNewItems(CreateItem(1))
            .WithClassification(ClassificationResult.Fallback("model", "boom", "{}"));
        fixture.NotificationPolicy.ShouldNotifyImmediately(Arg.Any<ClassificationResult>()).Returns(true);

        await fixture.Build().Invoke();

        await fixture.ImmediateNotifier.DidNotReceive().NotifyAsync(
            Arg.Any<RaindropItem>(), Arg.Any<ClassificationResult>(), Arg.Any<CancellationToken>());
    }

    // --- F-03 : isolation d'erreur par article ------------------------------------------------

    [Fact]
    public async Task Invoke_ExceptionOnThirdItem_KeepsProgressOnTheFirstTwo()
    {
        var fixture = new JobFixture()
            .WithNewItems(CreateItem(1), CreateItem(2), CreateItem(3))
            .WithClassification(CreateClassification(".NET", ["dotnet"]));

        fixture.ArticleRepository
            .UpsertAsync(Arg.Is<RaindropItem>(i => i.Id == 3), Arg.Any<ClassificationResult>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database is locked"));

        await fixture.Build().Invoke();

        // Avant F-03 l'exception remontait au catch du cycle et le high-water mark n'avançait pas du tout,
        // ce qui rejouait les articles 1 et 2 déjà écrits dans Raindrop.
        await fixture.PollingStateRepository.Received(1).UpdateAsync(
            Arg.Is<PollingState>(s => s.LastRaindropId == 2), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_ExceptionOnFirstItem_DoesNotAdvancePollingState()
    {
        var fixture = new JobFixture()
            .WithNewItems(CreateItem(1))
            .WithClassification(CreateClassification(".NET", ["dotnet"]));

        fixture.ArticleRepository
            .UpsertAsync(Arg.Any<RaindropItem>(), Arg.Any<ClassificationResult>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database is locked"));

        await fixture.Build().Invoke();

        await fixture.PollingStateRepository.DidNotReceive().UpdateAsync(Arg.Any<PollingState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_RaindropWriteBackFails_StillCountsTheItemAsProcessed()
    {
        var fixture = new JobFixture()
            .WithNewItems(CreateItem(1), CreateItem(2))
            .WithClassification(CreateClassification(".NET", ["dotnet"]));

        fixture.RaindropClient
            .UpdateRaindropAsync(1, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("502"));

        await fixture.Build().Invoke();

        // L'échec de write-back est déjà rattrapé et enregistré ; il ne doit pas bloquer le batch.
        await fixture.ArticleRepository.Received(1).RecordWriteBackAsync(1, false, false, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await fixture.PollingStateRepository.Received(1).UpdateAsync(
            Arg.Is<PollingState>(s => s.LastRaindropId == 2), Arg.Any<CancellationToken>());
    }

    // --- Notification immédiate ---------------------------------------------------------------

    [Fact]
    public async Task Invoke_PolicyTriggers_NotifiesAndRecordsIt()
    {
        var fixture = new JobFixture()
            .WithNewItems(CreateItem(1))
            .WithClassification(CreateClassification(".NET", ["dotnet"]));
        fixture.NotificationPolicy.ShouldNotifyImmediately(Arg.Any<ClassificationResult>()).Returns(true);

        await fixture.Build().Invoke();

        await fixture.ImmediateNotifier.Received(1).NotifyAsync(
            Arg.Is<RaindropItem>(i => i.Id == 1), Arg.Any<ClassificationResult>(), Arg.Any<CancellationToken>());
        await fixture.ArticleRepository.Received(1).MarkDiscordNotifiedAsync(1, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    // --- Cycle à vide -------------------------------------------------------------------------

    [Fact]
    public async Task Invoke_NoNewItems_DoesNotLearnTaxonomyNorTouchState()
    {
        var fixture = new JobFixture().WithNewItems();

        await fixture.Build().Invoke();

        await fixture.RaindropClient.DidNotReceive().GetTaxonomyAsync(Arg.Any<CancellationToken>());
        await fixture.PollingStateRepository.DidNotReceive().UpdateAsync(Arg.Any<PollingState>(), Arg.Any<CancellationToken>());
        await fixture.Classifier.DidNotReceive().ClassifyAsync(
            Arg.Any<RaindropItem>(), Arg.Any<RaindropTaxonomy>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_RaindropUnavailable_LogsAndDoesNotThrow()
    {
        var fixture = new JobFixture();
        fixture.RaindropClient
            .GetNewRaindropsAsync(Arg.Any<PollingState>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("401 Unauthorized"));

        // Un cycle en échec ne doit jamais faire tomber le worker : Coravel ne le replanifierait pas.
        await fixture.Build().Invoke();

        await fixture.PollingStateRepository.DidNotReceive().UpdateAsync(Arg.Any<PollingState>(), Arg.Any<CancellationToken>());
    }

    // --- F-06 : arrêt gracieux ----------------------------------------------------------------

    [Fact]
    public async Task Invoke_CancelledDuringBatch_PersistsProgressAnyway()
    {
        using var cts = new CancellationTokenSource();
        var fixture = new JobFixture()
            .WithNewItems(CreateItem(1), CreateItem(2))
            .WithClassification(CreateClassification(".NET", ["dotnet"]));

        fixture.Classifier
            .ClassifyAsync(Arg.Is<RaindropItem>(i => i.Id == 2), Arg.Any<RaindropTaxonomy>(), Arg.Any<CancellationToken>())
            .Returns<ClassificationResult>(_ =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });

        var job = fixture.Build();
        job.CancellationToken = cts.Token;

        await job.Invoke();

        // L'écriture du high-water mark ne passe pas par le token d'arrêt, sinon le batch entier
        // serait rejoué au redémarrage.
        await fixture.PollingStateRepository.Received(1).UpdateAsync(
            Arg.Is<PollingState>(s => s.LastRaindropId == 1), Arg.Any<CancellationToken>());
    }

    // --- Fixture ------------------------------------------------------------------------------

    private static RaindropItem CreateItem(long id, IReadOnlyList<string>? tags = null) => new(
        id,
        $"Article {id}",
        $"https://example.com/{id}",
        "extrait",
        null,
        tags ?? [],
        null,
        "example.com",
        "article",
        DateTimeOffset.UnixEpoch.AddDays(id),
        null);

    private static ClassificationResult CreateClassification(
        string? suggestedCollection,
        IReadOnlyList<string> tags,
        string reason = "raison") =>
        new(suggestedCollection, tags, RecommendedAction.ATester, Priority.Haute, reason, "model", "{}");

    private sealed class JobFixture
    {
        public IRaindropClient RaindropClient { get; } = Substitute.For<IRaindropClient>();
        public IPollingStateRepository PollingStateRepository { get; } = Substitute.For<IPollingStateRepository>();
        public IArticleRepository ArticleRepository { get; } = Substitute.For<IArticleRepository>();
        public IClassifier Classifier { get; } = Substitute.For<IClassifier>();
        public IImmediateNotifier ImmediateNotifier { get; } = Substitute.For<IImmediateNotifier>();
        public INotificationPolicy NotificationPolicy { get; } = Substitute.For<INotificationPolicy>();

        private bool _writeBack = true;

        public JobFixture()
        {
            PollingStateRepository.GetAsync(Arg.Any<CancellationToken>()).Returns(PollingState.Initial);
            RaindropClient.GetNewRaindropsAsync(Arg.Any<PollingState>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<RaindropItem>());
            RaindropClient.GetTaxonomyAsync(Arg.Any<CancellationToken>()).Returns(Taxonomy);
            NotificationPolicy.ShouldNotifyImmediately(Arg.Any<ClassificationResult>()).Returns(false);
        }

        public JobFixture WithTaxonomy(RaindropTaxonomy taxonomy)
        {
            RaindropClient.GetTaxonomyAsync(Arg.Any<CancellationToken>()).Returns(taxonomy);
            return this;
        }

        public JobFixture WithWriteBack(bool enabled)
        {
            _writeBack = enabled;
            return this;
        }

        public JobFixture WithNewItems(params RaindropItem[] items)
        {
            RaindropClient.GetNewRaindropsAsync(Arg.Any<PollingState>(), Arg.Any<CancellationToken>()).Returns(items);
            return this;
        }

        public JobFixture WithClassification(ClassificationResult result)
        {
            Classifier.ClassifyAsync(Arg.Any<RaindropItem>(), Arg.Any<RaindropTaxonomy>(), Arg.Any<CancellationToken>())
                .Returns(result);
            return this;
        }

        public JobFixture WithClassificationSequence(params ClassificationResult[] results)
        {
            Classifier.ClassifyAsync(Arg.Any<RaindropItem>(), Arg.Any<RaindropTaxonomy>(), Arg.Any<CancellationToken>())
                .Returns(results[0], results[1..]);
            return this;
        }

        public UnsortedClassificationJob Build() => new(
            RaindropClient,
            PollingStateRepository,
            ArticleRepository,
            Classifier,
            ImmediateNotifier,
            NotificationPolicy,
            MsOptions.Create(new WorkerOptions { WriteBackToRaindrop = _writeBack }),
            NullLogger<UnsortedClassificationJob>.Instance);
    }
}
