using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LoreAI.Core.Enums;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;
using LoreAI.Core.Services;
using LoreAI.Infrastructure.Gmail.Dto;

namespace LoreAI.Infrastructure.Gmail;

/// <summary>
/// Consomme l'API Gmail via <c>HttpClient</c> brut (même patron que <see cref="Raindrop.RaindropClient"/>) :
/// refresh du token OAuth par appel, résolution du label configuré, puis <c>users.history.list</c> incrémental
/// sur le curseur <c>historyId</c> (lot 8, #49). Contrairement à Raindrop, ce curseur n'est pas dérivable du
/// dernier <see cref="Item"/> retourné (c'est un curseur de boîte mail, pas de lien) : cette classe persiste
/// elle-même sa progression via <see cref="IPollingStateRepository"/>, une fois le lot de messages traité
/// avec succès — l'appelant n'a rien à faire de plus qu'avec Raindrop.
/// </summary>
public sealed class GmailIngester : IGmailIngester
{
    private const int MaxHistoryPagesPerCycle = 50;

    private readonly HttpClient _httpClient;
    private readonly GoogleOAuthOptions _options;
    private readonly IEmailLinkExtractor _emailLinkExtractor;
    private readonly IPollingStateRepository _pollingStateRepository;
    private readonly ILogger<GmailIngester> _logger;

    public GmailIngester(
        HttpClient httpClient,
        IOptions<GoogleOAuthOptions> options,
        IEmailLinkExtractor emailLinkExtractor,
        IPollingStateRepository pollingStateRepository,
        ILogger<GmailIngester> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _emailLinkExtractor = emailLinkExtractor;
        _pollingStateRepository = pollingStateRepository;
        _logger = logger;

        _httpClient.BaseAddress ??= new Uri(_options.ApiBaseUrl.TrimEnd('/') + "/");
    }

    public SourceType SourceType => SourceType.Newsletter;

    public async Task<IReadOnlyList<Item>> GetNewItemsAsync(PollingState lastState, CancellationToken cancellationToken)
    {
        // Premier démarrage : jamais de backfill automatique, même logique que le « First-run caveat »
        // documenté pour Raindrop (CLAUDE.md) — le curseur se seed manuellement (voir README).
        if (lastState.LastSourceItemId is null)
        {
            _logger.LogWarning(
                "Aucun curseur historyId enregistré pour la source Newsletter : aucun message ne sera récupéré. " +
                "Seeder PollingStates manuellement (voir README) pour amorcer le connecteur Gmail.");
            return [];
        }

        await AuthenticateAsync(cancellationToken);

        var labelId = await ResolveLabelIdAsync(cancellationToken);
        if (labelId is null)
        {
            _logger.LogWarning("Label Gmail « {Label} » introuvable : aucun message ne sera récupéré.", _options.Label);
            return [];
        }

        var (messageIds, newHistoryId) = await ListNewMessageIdsAsync(lastState.LastSourceItemId, labelId, cancellationToken);

        var items = new List<Item>();
        foreach (var messageId in messageIds)
        {
            items.AddRange(await ExtractItemsFromMessageAsync(messageId, cancellationToken));
        }

        if (newHistoryId is not null)
        {
            await _pollingStateRepository.UpdateAsync(
                new PollingState(SourceType.Newsletter, newHistoryId, null, DateTimeOffset.UtcNow),
                cancellationToken);
        }

        return items;
    }

    private async Task AuthenticateAsync(CancellationToken cancellationToken)
    {
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, _options.TokenUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["refresh_token"] = _options.RefreshToken,
                ["grant_type"] = "refresh_token",
            }),
        };

        using var tokenResponse = await _httpClient.SendAsync(tokenRequest, cancellationToken);
        tokenResponse.EnsureSuccessStatusCode();

        var token = await tokenResponse.Content.ReadFromJsonAsync<TokenResponseDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Réponse de rafraîchissement du token Google vide ou invalide.");

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
    }

    private async Task<string?> ResolveLabelIdAsync(CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync("users/me/labels", cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<LabelsListDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Réponse Gmail /users/me/labels vide ou invalide.");

        return payload.Labels.FirstOrDefault(l => string.Equals(l.Name, _options.Label, StringComparison.Ordinal))?.Id;
    }

    private async Task<(List<string> MessageIds, string? NewHistoryId)> ListNewMessageIdsAsync(
        string startHistoryId, string labelId, CancellationToken cancellationToken)
    {
        var messageIds = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? newHistoryId = null;
        string? pageToken = null;
        var page = 0;

        do
        {
            var url = $"users/me/history?startHistoryId={Uri.EscapeDataString(startHistoryId)}&labelId={Uri.EscapeDataString(labelId)}&historyTypes=messageAdded"
                + (pageToken is null ? string.Empty : $"&pageToken={Uri.EscapeDataString(pageToken)}");

            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<HistoryListDto>(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("Réponse Gmail /users/me/history vide ou invalide.");

            foreach (var id in payload.History.SelectMany(h => h.MessagesAdded).Select(m => m.Message.Id))
            {
                if (seen.Add(id))
                {
                    messageIds.Add(id);
                }
            }

            newHistoryId ??= payload.HistoryId;
            if (payload.HistoryId is not null)
            {
                newHistoryId = payload.HistoryId;
            }

            pageToken = payload.NextPageToken;
            page++;
        }
        while (pageToken is not null && page < MaxHistoryPagesPerCycle);

        if (page >= MaxHistoryPagesPerCycle)
        {
            _logger.LogWarning(
                "Plafond de {MaxPages} pages atteint pour /users/me/history : {Count} messages récupérés, le reste sera repris au prochain cycle.",
                MaxHistoryPagesPerCycle,
                messageIds.Count);
        }

        return (messageIds, newHistoryId);
    }

    private async Task<IReadOnlyList<Item>> ExtractItemsFromMessageAsync(string messageId, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"users/me/messages/{messageId}?format=full", cancellationToken);
        response.EnsureSuccessStatusCode();

        var message = await response.Content.ReadFromJsonAsync<MessageDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException($"Réponse Gmail /users/me/messages/{messageId} vide ou invalide.");

        var parsed = GmailMessageParser.Parse(message.Payload);
        var candidateUrls = EmailLinkNoiseFilter.Filter(parsed.CandidateUrls);

        // Court-circuit légitime (cf. IEmailLinkExtractor) : aucune URL exploitable après le filtre
        // heuristique, pas d'appel LLM pour ce message.
        if (candidateUrls.Count == 0)
        {
            return [];
        }

        var extractedLinks = await _emailLinkExtractor.ExtractAsync(parsed.Subject, parsed.Body, candidateUrls, cancellationToken);
        var capturedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(message.InternalDate, CultureInfo.InvariantCulture));

        return extractedLinks
            .Select((link, index) => new Item(
                SourceType.Newsletter,
                $"{messageId}:{index.ToString(CultureInfo.InvariantCulture)}",
                link.Url,
                link.Title,
                null,
                null,
                [],
                capturedAtUtc))
            .ToList();
    }
}
