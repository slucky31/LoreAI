using System.Globalization;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LoreAI.Core.Enums;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;
using LoreAI.Infrastructure.Feed.Dto;

namespace LoreAI.Infrastructure.Feed;

/// <summary>
/// Consomme l'API REST d'une instance Miniflux auto-hébergée (lot 7, #48) : Miniflux possède la liste
/// d'abonnements, parse les flux et sert d'interface de lecture humaine — cette classe ne fait que lire
/// ses nouvelles entrées, tous flux confondus, via <c>GET /v1/entries</c>. Curseur = id d'entrée Miniflux
/// (entier monotone côté serveur, cf. <c>after_entry_id</c>), même patron que <see cref="Gmail.GmailIngester"/> :
/// cette classe persiste elle-même sa progression via <see cref="IPollingStateRepository"/>. Jamais de
/// write-back vers Miniflux (jamais de <c>PUT /v1/entries</c>) : une source Feed n'est jamais réécrite
/// (ADR 0012), et Miniflux reste l'interface de lecture, indépendante de l'ingestion LoreAI.
/// </summary>
public sealed class MinifluxIngester : IFeedIngester
{
    private const int MaxPagesPerCycle = 50;
    private const int PageSize = 100;

    private readonly HttpClient _httpClient;
    private readonly IPollingStateRepository _pollingStateRepository;
    private readonly ILogger<MinifluxIngester> _logger;

    public MinifluxIngester(HttpClient httpClient, IOptions<MinifluxOptions> options, IPollingStateRepository pollingStateRepository, ILogger<MinifluxIngester> logger)
    {
        _httpClient = httpClient;
        _pollingStateRepository = pollingStateRepository;
        _logger = logger;

        var minifluxOptions = options.Value;
        _httpClient.BaseAddress ??= new Uri(minifluxOptions.BaseUrl.TrimEnd('/') + "/");
        _httpClient.DefaultRequestHeaders.Remove("X-Auth-Token");
        _httpClient.DefaultRequestHeaders.Add("X-Auth-Token", minifluxOptions.ApiToken);
    }

    public SourceType SourceType => SourceType.Feed;

    public async Task<IReadOnlyList<Item>> GetNewItemsAsync(PollingState lastState, CancellationToken cancellationToken)
    {
        // Premier démarrage : jamais de backfill automatique, même choix que GmailIngester (pas celui de
        // Raindrop) — sans garde, un premier cycle remonterait l'historique complet de tous les flux
        // souscrits dans Miniflux. Le curseur se seed manuellement (voir README/guide de déploiement).
        if (lastState.LastSourceItemId is null)
        {
            _logger.LogWarning(
                "Aucun curseur d'entrée enregistré pour la source Feed : aucune entrée ne sera récupérée. " +
                "Seeder PollingStates manuellement (voir README) pour amorcer le connecteur Miniflux.");
            return [];
        }

        var (entries, lastEntryId) = await ListNewEntriesAsync(lastState.LastSourceItemId, cancellationToken);

        var items = entries
            .Select(entry => new Item(
                SourceType.Feed,
                entry.Id.ToString(CultureInfo.InvariantCulture),
                entry.Url,
                entry.Title,
                null,
                null,
                [],
                entry.PublishedAt))
            .ToList();

        if (lastEntryId is not null)
        {
            await _pollingStateRepository.UpdateAsync(
                new PollingState(SourceType.Feed, lastEntryId, null, DateTimeOffset.UtcNow),
                cancellationToken);
        }

        return items;
    }

    private async Task<(List<EntryDto> Entries, string? LastEntryId)> ListNewEntriesAsync(string afterEntryId, CancellationToken cancellationToken)
    {
        var entries = new List<EntryDto>();
        var cursor = afterEntryId;
        var page = 0;

        do
        {
            var url = $"v1/entries?after_entry_id={Uri.EscapeDataString(cursor)}&order=id&direction=asc&limit={PageSize.ToString(CultureInfo.InvariantCulture)}";

            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<EntriesResponseDto>(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("Réponse Miniflux /v1/entries vide ou invalide.");

            entries.AddRange(payload.Entries);
            if (payload.Entries.Count > 0)
            {
                cursor = payload.Entries[^1].Id.ToString(CultureInfo.InvariantCulture);
            }

            page++;
            if (payload.Entries.Count < PageSize)
            {
                break;
            }
        }
        while (page < MaxPagesPerCycle);

        if (page >= MaxPagesPerCycle)
        {
            _logger.LogWarning(
                "Plafond de {MaxPages} pages atteint pour /v1/entries : {Count} entrées récupérées, le reste sera repris au prochain cycle.",
                MaxPagesPerCycle,
                entries.Count);
        }

        return (entries, entries.Count > 0 ? cursor : null);
    }
}
