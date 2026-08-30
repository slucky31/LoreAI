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
/// Symétrique à <see cref="MinifluxIngester"/> (lot 7) mais scindé sur une <b>catégorie</b> Miniflux
/// quelconque (lot 9, #50, redesign) : chaque sujet de veille a la sienne, provisionnée par
/// <c>WatchTopicProvisioner</c>. Lecteur pur, sans état — le curseur (par sujet, pas par
/// <see cref="SourceType"/>) est géré par l'appelant via <c>IWatchTopicRepository</c>, contrairement à
/// <see cref="MinifluxIngester"/> qui persiste lui-même son <c>PollingState</c>. Jamais de write-back (ni
/// Miniflux ni Raindrop côté lecture) : cette classe ne fait que lire.
/// </summary>
public sealed class MinifluxWatchIngester : IMinifluxCategoryReader
{
    private const int MaxPagesPerCycle = 50;
    private const int PageSize = 100;

    private readonly HttpClient _httpClient;
    private readonly ILogger<MinifluxWatchIngester> _logger;

    public MinifluxWatchIngester(HttpClient httpClient, IOptions<MinifluxOptions> minifluxOptions, ILogger<MinifluxWatchIngester> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var options = minifluxOptions.Value;
        _httpClient.BaseAddress ??= new Uri(options.BaseUrl.TrimEnd('/') + "/");
        _httpClient.DefaultRequestHeaders.Remove("X-Auth-Token");
        _httpClient.DefaultRequestHeaders.Add("X-Auth-Token", options.ApiToken);
    }

    public async Task<(IReadOnlyList<Item> Items, string? LastEntryId)> GetNewEntriesAsync(int categoryId, string afterEntryId, CancellationToken cancellationToken)
    {
        var entries = new List<EntryDto>();
        var cursor = afterEntryId;
        var page = 0;

        do
        {
            var url = $"v1/categories/{categoryId.ToString(CultureInfo.InvariantCulture)}/entries" +
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
                "Plafond de {MaxPages} pages atteint pour la catégorie {CategoryId} : {Count} entrées récupérées, le reste sera repris au prochain cycle.",
                MaxPagesPerCycle,
                categoryId,
                entries.Count);
        }

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

        return (items, entries.Count > 0 ? cursor : null);
    }
}
