using Microsoft.Extensions.Logging;
using LoreAI.Core.Enums;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;

namespace LoreAI.Infrastructure.Content;

/// <summary>
/// Récupération best-effort du contenu réel d'un article (S1, lot 4). Contrairement à
/// <c>RaindropClient</c>/<c>AnthropicClassifier</c>, l'URL cible est arbitraire à chaque appel : pas de
/// <c>BaseAddress</c>. Aucun échec attendu (HTTP, timeout, contenu non-HTML, extraction vide) ne remonte
/// en exception — même philosophie que <see cref="LoreAI.Core.Models.ClassificationResult.Fallback"/>.
/// </summary>
public sealed class HttpContentFetcher : IContentFetcher
{
    /// <summary>Politesse envers des sites tiers inconnus : on n'engloutit pas un binaire mal étiqueté.</summary>
    private const long MaxContentLengthBytes = 5 * 1024 * 1024;

    private const string UserAgent = "LoreAI/1.0 (+https://github.com/slucky31/LoreAI)";

    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpContentFetcher> _logger;

    public HttpContentFetcher(HttpClient httpClient, ILogger<HttpContentFetcher> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        }
    }

    public async Task<ContentFetchResult> FetchAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("Fetch de contenu échoué pour {Url} : HTTP {StatusCode}.", url, response.StatusCode);
                }
                return new ContentFetchResult(ContentFetchStatus.HttpError, null, null);
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType is not null && !contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
            {
                return new ContentFetchResult(ContentFetchStatus.UnsupportedContentType, null, null);
            }

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength is > MaxContentLengthBytes)
            {
                return new ContentFetchResult(ContentFetchStatus.UnsupportedContentType, null, null);
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var (text, wordCount) = ArticleTextExtractor.Extract(html);

            return text is null
                ? new ContentFetchResult(ContentFetchStatus.ExtractionEmpty, null, null)
                : new ContentFetchResult(ContentFetchStatus.Success, text, wordCount);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(ex, "Fetch de contenu échoué pour {Url}.", url);
            }
            return new ContentFetchResult(IsTimeout(ex, cancellationToken) ? ContentFetchStatus.Timeout : ContentFetchStatus.HttpError, null, null);
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(ex, "Extraction de contenu échouée pour {Url}.", url);
            }
            return new ContentFetchResult(ContentFetchStatus.Error, null, null);
        }
    }

    /// <summary>Même distinction que <c>AnthropicClassifier.IsTransportFailure</c> : un arrêt de l'application n'est pas un échec.</summary>
    private static bool IsTransportFailure(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        OperationCanceledException when cancellationToken.IsCancellationRequested => false,
        OperationCanceledException => true,
        HttpRequestException or TimeoutException => true,
        _ => false,
    };

    private static bool IsTimeout(Exception exception, CancellationToken cancellationToken) =>
        exception is TimeoutException || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);
}
