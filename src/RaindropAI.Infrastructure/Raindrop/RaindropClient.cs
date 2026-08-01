using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
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
    /// <summary>
    /// Garde-fou contre une pagination qui ne se terminerait jamais (serveur ignorant le paramètre
    /// <c>page</c>). À 50 éléments par page, cela couvre 10 000 articles en un cycle.
    /// </summary>
    private const int MaxPagesPerCycle = 200;

    private readonly HttpClient _httpClient;
    private readonly RaindropApiOptions _options;
    private readonly ILogger<RaindropClient> _logger;

    public RaindropClient(HttpClient httpClient, IOptions<RaindropApiOptions> options, ILogger<RaindropClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        _httpClient.BaseAddress ??= new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.Token);
    }

    public async Task<IReadOnlyList<RaindropItem>> GetNewRaindropsAsync(PollingState lastState, CancellationToken cancellationToken)
    {
        var collected = new List<RaindropItem>();
        var page = 0;

        while (page < MaxPagesPerCycle)
        {
            var url = $"raindrops/{_options.CollectionId}?sort=-created&perpage={_options.PageSize}&page={page}";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<RaindropsPageDto>(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("Réponse Raindrop vide ou invalide.");

            // Une page vide est la seule fin de liste non ambiguë. Se fier à « page plus courte que
            // demandé » confondrait la fin de liste avec un serveur qui rend moins d'éléments que
            // le perpage demandé — et ferait perdre silencieusement tout ce qui suit.
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

            if (reachedKnownItem)
            {
                break;
            }

            page++;
        }

        if (page >= MaxPagesPerCycle)
        {
            _logger.LogWarning(
                "Plafond de {MaxPages} pages atteint pour la collection {CollectionId} : {Count} articles récupérés, " +
                "le reste sera repris au prochain cycle.",
                MaxPagesPerCycle,
                _options.CollectionId,
                collected.Count);
        }

        collected.Reverse(); // du plus ancien au plus récent, prêt pour le traitement séquentiel
        return collected;
    }

    public async Task<RaindropTaxonomy> GetTaxonomyAsync(CancellationToken cancellationToken)
    {
        var rootCollections = await GetCollectionsAsync("collections", cancellationToken);
        var nestedCollections = await GetCollectionsAsync("collections/childrens", cancellationToken);
        var collections = rootCollections
            .Concat(nestedCollections)
            .Select(dto => new RaindropCollection(dto.Id, dto.Title))
            .ToList();

        var tagsResponse = await _httpClient.GetAsync("tags", cancellationToken);
        tagsResponse.EnsureSuccessStatusCode();
        var tagsPayload = await tagsResponse.Content.ReadFromJsonAsync<TagsPageDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Réponse Raindrop /tags vide ou invalide.");
        var tags = tagsPayload.Items.Select(t => new RaindropTag(t.Id, t.Count)).ToList();

        return new RaindropTaxonomy(collections, tags);
    }

    public async Task UpdateRaindropAsync(long raindropId, IReadOnlyCollection<string> tags, string note, long? collectionId, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["tags"] = tags,
            ["note"] = note,
        };

        if (collectionId is not null)
        {
            body["collection"] = new Dictionary<string, object> { ["$id"] = collectionId.Value };
        }

        var response = await _httpClient.PutAsJsonAsync($"raindrop/{raindropId}", body, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<List<CollectionDto>> GetCollectionsAsync(string path, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<CollectionsPageDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException($"Réponse Raindrop /{path} vide ou invalide.");
        return payload.Items;
    }

    /// <summary>
    /// Les deux critères sont évalués indépendamment. Auparavant un <c>LastRaindropId</c> nul court-circuitait
    /// la fonction avant même le test de date : un état de polling amorcé avec la seule date — le cas naturel
    /// (« ignorer tout ce qui est antérieur à aujourd'hui », sans avoir à retrouver l'id du dernier raindrop) —
    /// était donc totalement ignoré, et l'historique complet remonté.
    /// </summary>
    private static bool IsAlreadyKnown(RaindropDto dto, PollingState lastState)
    {
        if (lastState.LastRaindropId is not null && dto.Id == lastState.LastRaindropId)
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
