using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using LoreAI.Core.Enums;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;
using LoreAI.Worker.Services;

namespace LoreAI.Worker.Tests.Services;

/// <summary>Couvre l'orchestration de l'indexation en lecture seule de la bibliothèque (lot 1, #42) : reprise sur interruption, garde anti-doublon, plafond défensif.</summary>
public class LibraryIndexingJobTests
{
    [Fact]
    public void Constructor_HasNoDependencyOnClassificationOrWriteBackTypes()
    {
        // Le caractère lecture seule est garanti par la forme du constructeur, pas seulement par
        // convention : aucun de ces types ne doit pouvoir y entrer, même par erreur future.
        var parameterTypes = typeof(LibraryIndexingJob)
            .GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ToList();

        Assert.DoesNotContain(typeof(IClassifier), parameterTypes);
        Assert.DoesNotContain(typeof(IArticleRepository), parameterTypes);
        Assert.DoesNotContain(typeof(ICycleRunRepository), parameterTypes);
    }

    [Fact]
    public async Task Invoke_FreshStart_PagesUntilEmptyThenMarksPassComplete()
    {
        var fixture = new JobFixture();
        fixture.RaindropClient.GetLibraryPageAsync(0, Arg.Any<CancellationToken>()).Returns([CreateLibraryItem(1)]);
        fixture.RaindropClient.GetLibraryPageAsync(1, Arg.Any<CancellationToken>()).Returns(Array.Empty<LibraryItem>());

        await fixture.Build().Invoke();

        await fixture.LibraryItemRepository.Received(1).UpsertPageAsync(
            Arg.Is<IReadOnlyList<LibraryItem>>(items => items!.Count == 1), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await fixture.IndexStateRepository.Received(1).UpdateAsync(
            Arg.Is<LibraryIndexState>(s => s!.ResumePage == null && s.LastFullPassCompletedUtc != null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_ResumesFromPersistedPage()
    {
        var fixture = new JobFixture()
            .WithState(new LibraryIndexState(SourceType.Raindrop, 3, DateTimeOffset.UtcNow.AddMinutes(-5), null, DateTimeOffset.UtcNow));
        fixture.RaindropClient.GetLibraryPageAsync(3, Arg.Any<CancellationToken>()).Returns(Array.Empty<LibraryItem>());

        await fixture.Build().Invoke();

        await fixture.RaindropClient.Received(1).GetLibraryPageAsync(3, Arg.Any<CancellationToken>());
        await fixture.RaindropClient.DidNotReceive().GetLibraryPageAsync(0, Arg.Any<CancellationToken>());
    }

    /// <summary>Garde défensive contre une pagination sans fin — voir la constante dans <see cref="LibraryIndexingJob"/>.</summary>
    [Fact]
    public async Task Invoke_HitsMaxPagesPerInvocation_PersistsResumePageAndStopsWithoutError()
    {
        const int maxPagesPerInvocation = 500;
        var fixture = new JobFixture();
        fixture.RaindropClient.GetLibraryPageAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([CreateLibraryItem(1)]);

        var exception = await Record.ExceptionAsync(() => fixture.Build().Invoke());

        Assert.Null(exception);
        await fixture.RaindropClient.Received(maxPagesPerInvocation).GetLibraryPageAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await fixture.IndexStateRepository.Received(1).UpdateAsync(
            Arg.Is<LibraryIndexState>(s => s!.ResumePage == maxPagesPerInvocation), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_TransientException_LeavesStateAtLastSuccessfullyPersistedPage()
    {
        var fixture = new JobFixture();
        fixture.RaindropClient.GetLibraryPageAsync(0, Arg.Any<CancellationToken>()).Returns([CreateLibraryItem(1)]);
        fixture.RaindropClient.GetLibraryPageAsync(1, Arg.Any<CancellationToken>()).Returns([CreateLibraryItem(2)]);
        fixture.RaindropClient.GetLibraryPageAsync(2, Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("502", null, HttpStatusCode.BadGateway));

        var exception = await Record.ExceptionAsync(() => fixture.Build().Invoke());

        Assert.Null(exception);
        await fixture.LibraryItemRepository.Received(2).UpsertPageAsync(
            Arg.Any<IReadOnlyList<LibraryItem>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await fixture.IndexStateRepository.Received(1).UpdateAsync(
            Arg.Is<LibraryIndexState>(s => s!.ResumePage == 2), Arg.Any<CancellationToken>());
    }

    // --- Garde anti-doublon (24h) ---------------------------------------------------------------

    [Fact]
    public async Task Invoke_LastFullPassCompletedRecently_SkipsWithoutCallingApi()
    {
        var fixture = new JobFixture()
            .WithState(new LibraryIndexState(SourceType.Raindrop, null, DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow));

        await fixture.Build().Invoke();

        await fixture.RaindropClient.DidNotReceive().GetLibraryPageAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await fixture.IndexStateRepository.DidNotReceive().UpdateAsync(Arg.Any<LibraryIndexState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_LastFullPassCompletedOverADayAgo_StartsNewPass()
    {
        var fixture = new JobFixture()
            .WithState(new LibraryIndexState(SourceType.Raindrop, null, DateTimeOffset.UtcNow.AddHours(-26), DateTimeOffset.UtcNow.AddHours(-25), DateTimeOffset.UtcNow));
        fixture.RaindropClient.GetLibraryPageAsync(0, Arg.Any<CancellationToken>()).Returns(Array.Empty<LibraryItem>());

        await fixture.Build().Invoke();

        await fixture.RaindropClient.Received(1).GetLibraryPageAsync(0, Arg.Any<CancellationToken>());
    }

    private static LibraryItem CreateLibraryItem(long id) => new(
        new Item(SourceType.Raindrop, id.ToString(CultureInfo.InvariantCulture), $"https://example.com/{id}", $"Article {id}", null, null, [], DateTimeOffset.UnixEpoch),
        ItemOrigin.Library,
        null,
        false,
        false,
        null,
        null);

    private sealed class JobFixture
    {
        public IRaindropClient RaindropClient { get; } = Substitute.For<IRaindropClient>();
        public ILibraryIndexStateRepository IndexStateRepository { get; } = Substitute.For<ILibraryIndexStateRepository>();
        public ILibraryItemRepository LibraryItemRepository { get; } = Substitute.For<ILibraryItemRepository>();

        public JobFixture()
        {
            IndexStateRepository.GetAsync(Arg.Any<SourceType>(), Arg.Any<CancellationToken>())
                .Returns(LibraryIndexState.Initial(SourceType.Raindrop));
            RaindropClient.GetLibraryPageAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<LibraryItem>());
        }

        public JobFixture WithState(LibraryIndexState state)
        {
            IndexStateRepository.GetAsync(Arg.Any<SourceType>(), Arg.Any<CancellationToken>()).Returns(state);
            return this;
        }

        public LibraryIndexingJob Build() => new(RaindropClient, IndexStateRepository, LibraryItemRepository, NullLogger<LibraryIndexingJob>.Instance);
    }
}
