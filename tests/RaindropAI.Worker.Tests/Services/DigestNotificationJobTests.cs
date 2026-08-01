using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RaindropAI.Core.Enums;
using RaindropAI.Core.Interfaces;
using RaindropAI.Core.Models;
using RaindropAI.Worker.Services;

namespace RaindropAI.Worker.Tests.Services;

public class DigestNotificationJobTests
{
    private readonly IArticleRepository _articleRepository = Substitute.For<IArticleRepository>();
    private readonly IDigestNotifier _digestNotifier = Substitute.For<IDigestNotifier>();

    [Fact]
    public async Task Invoke_NothingPending_DoesNotSendAnything()
    {
        _articleRepository.GetUnsentDigestItemsAsync(Arg.Any<CancellationToken>()).Returns([]);

        await CreateJob().Invoke();

        await _digestNotifier.DidNotReceive().SendDigestAsync(Arg.Any<IReadOnlyList<ClassifiedArticle>>(), Arg.Any<CancellationToken>());
        await _articleRepository.DidNotReceive().MarkDigestSentAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_PendingArticles_SendsThenMarksThemAsSent()
    {
        _articleRepository.GetUnsentDigestItemsAsync(Arg.Any<CancellationToken>())
            .Returns([CreateArticle(1), CreateArticle(2)]);

        await CreateJob().Invoke();

        await _digestNotifier.Received(1).SendDigestAsync(
            Arg.Is<IReadOnlyList<ClassifiedArticle>>(a => a!.Count == 2), Arg.Any<CancellationToken>());
        await _articleRepository.Received(1).MarkDigestSentAsync(
            Arg.Is<IReadOnlyCollection<long>>(ids => ids!.Contains(1) && ids!.Contains(2)),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Sans envoi réussi, rien ne doit être marqué : le digest est un filet, il ne doit rien perdre.</summary>
    [Fact]
    public async Task Invoke_SendFails_DoesNotMarkArticlesAsSent()
    {
        _articleRepository.GetUnsentDigestItemsAsync(Arg.Any<CancellationToken>()).Returns([CreateArticle(1)]);
        _digestNotifier
            .SendDigestAsync(Arg.Any<IReadOnlyList<ClassifiedArticle>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("SMTP indisponible"));

        await CreateJob().Invoke();

        await _articleRepository.DidNotReceive().MarkDigestSentAsync(
            Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    private DigestNotificationJob CreateJob() =>
        new(_articleRepository, _digestNotifier, NullLogger<DigestNotificationJob>.Instance);

    private static ClassifiedArticle CreateArticle(long id)
    {
        var item = new RaindropItem(
            id, $"Article {id}", $"https://example.com/{id}", null, null, [], null, "example.com", "article",
            DateTimeOffset.UnixEpoch.AddDays(id), null);

        var classification = new ClassificationResult(
            ".NET", ["dotnet"], RecommendedAction.ALire, Priority.Moyenne, "raison", "model", "{}");

        return new ClassifiedArticle(item, classification, DateTimeOffset.UtcNow, Moved: true, null, null);
    }
}
