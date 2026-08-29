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
/// Symétrique à <see cref="MinifluxIngester"/> (lot 7) mais scindé sur une <b>catégorie</b> Miniflux dédiée
/// (<see cref="WatchOptions.MinifluxCategoryId"/>) : les flux RSS de recherche de la veille (C4, lot 9, #50)
/// vivent dans une catégorie séparée des flux de lecture personnelle, pour ne jamais les faire passer par le
/// pipeline de classification Raindrop de <c>FeedIngestionJob</c>. Curseur propre (<see cref="SourceType.Watch"/>),
/// même patron <c>after_entry_id</c> que l'existant. Jamais de write-back (ni Miniflux ni Raindrop) : une
/// entrée de veille n'est même pas un <see cref="Item"/> persisté, elle n'est qu'évaluée puis oubliée.
/// </summary>
public sealed class MinifluxWatchIngester : ISourceIngester
{
    private const int MaxPagesPerCycle = 50;
    private const int PageSize = 100;

    private readonly HttpClient _httpClient;
    private readonly WatchOptions _watchOptions;
    private readonly IPollingStateRepository _pollingStateRepository;
    private readonly ILogger<MinifluxWatchIngester> _logger;

    public MinifluxWatchIngester(
        HttpClient httpClient,
        IOptions<MinifluxOptions> minifluxOptions,
        IOptions<WatchOptions> watchOptions,
        IPollingStateRepository pollingStateRepository,
        ILogger<MinifluxWatchIngester> logger)
    {
        _httpClient = httpClient;
        _watchOptions = watchOptions.Value;
        _pollingStateRepository = pollingStateRepository;
        _logger = logger;

        var options = minifluxOptions.Value;
        _httpClient.BaseAddress ??= new Uri(options.BaseUrl.TrimEnd('/') + "/");
        _httpClient.DefaultRequestHeaders.Remove("X-Auth-Token");
        _httpClient.DefaultRequestHeaders.Add("X-Auth-Token", options.ApiToken);
    }

    public SourceType SourceType => SourceType.Watch;

    public async Task<IReadOnlyList<Item>> GetNewItemsAsync(PollingState lastState, CancellationToken cancellationToken)
    {
        // Même garde qu'IFeedIngester : jamais de backfill automatique au premier démarrage. Le curseur se
        // seed manuellement (voir README) pour amorcer le connecteur.
        if (lastState.LastSourceItemId is null)
        {
            _logger.LogWarning(
                "Aucun curseur d'entrée enregistré pour la source Watch : aucune entrée ne sera récupérée. " +
                "Seeder PollingStates manuellement (voir README) pour amorcer la veille.");
            return [];
        }

        var (entries, lastEntryId) = await ListNewEntriesAsync(lastState.LastSourceItemId, cancellationToken);

        var items = entries
            .Select(entry => new Item(
                SourceType.Watch,
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
                new PollingState(SourceType.Watch, lastEntryId, null, DateTimeOffset.UtcNow),
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
            var url = $"v1/categories/{_watchOptions.MinifluxCategoryId.ToString(CultureInfo.InvariantCulture)}/entries" +
                $"?after_entry_id={Uri.EscapeDataString(cursor)}&order=id&direction=asc&limit={PageSize.ToString(CultureInfo.InvariantCulture)}";

            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<EntriesResponseDto>(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("Réponse Miniflux /v1/categories/{id}/entries vide ou invalide.");

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
                "Plafond de {MaxPages} pages atteint pour la catégorie de veille : {Count} entrées récupérées, le reste sera repris au prochain cycle.",
                MaxPagesPerCycle,
                entries.Count);
        }

        return (entries, entries.Count > 0 ? cursor : null);
    }
}
