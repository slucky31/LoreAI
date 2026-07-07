using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using RaindropAI.Core.Interfaces;
using RaindropAI.Core.Models;
using RaindropAI.Infrastructure.Raindrop.Dto;

namespace RaindropAI.Infrastructure.Raindrop;

/// <summary>
/// Consomme l'API REST Raindrop.io. Pas de webhook disponible côté API (vérifié dans la doc officielle) :
/// stratégie de polling avec high-water mark, cf. ADR 0003.
/// </summary>
public sealed class RaindropClient : IRaindropClient
{
    private readonly HttpClient _httpClient;
    private readonly RaindropApiOptions _options;

    public RaindropClient(HttpClient httpClient, IOptions<RaindropApiOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;

        _httpClient.BaseAddress ??= new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.Token);
    }

    public async Task<IReadOnlyList<RaindropItem>> GetNewRaindropsAsync(PollingState lastState, CancellationToken cancellationToken)
    {
        var collected = new List<RaindropItem>();
        var page = 0;

        while (true)
        {
            var url = $"raindrops/{_options.CollectionId}?sort=-created&perpage={_options.PageSize}&page={page}";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<RaindropsPageDto>(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("Réponse Raindrop vide ou invalide.");

            if (payload.Items.Count == 0)
            {
                break;
            }

            var reachedKnownItem = false;
            foreach (var dto in payload.Items)
            {
                if (IsAlreadyKnown(dto, lastState))
                {
                    reachedKnownItem = true;
                    break;
                }

                collected.Add(MapToRaindropItem(dto));
            }

            if (reachedKnownItem || payload.Items.Count < _options.PageSize)
            {
                break;
            }

            page++;
        }

        collected.Reverse(); // du plus ancien au plus récent, prêt pour le traitement séquentiel
        return collected;
    }

    public async Task UpdateRaindropAsync(long raindropId, IReadOnlyCollection<string> tags, string note, CancellationToken cancellationToken)
    {
        var body = new { tags, note };
        var response = await _httpClient.PutAsJsonAsync($"raindrop/{raindropId}", body, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static bool IsAlreadyKnown(RaindropDto dto, PollingState lastState)
    {
        if (lastState.LastRaindropId is null)
        {
            return false;
        }

        if (dto.Id == lastState.LastRaindropId)
        {
            return true;
        }

        return lastState.LastCreatedUtc is not null && dto.Created <= lastState.LastCreatedUtc;
    }

    private static RaindropItem MapToRaindropItem(RaindropDto dto) => new(
        dto.Id,
        dto.Title,
        dto.Link,
        dto.Excerpt,
        dto.Note,
        dto.Tags,
        dto.Collection?.Id,
        dto.Domain,
        dto.Type,
        dto.Created,
        dto.LastUpdate);
}
