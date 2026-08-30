using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;
using LoreAI.Infrastructure.Feed;

namespace LoreAI.Infrastructure.Watch;

/// <summary>
/// Provisionne un sujet de veille (C4, lot 9, #50, redesign) : crée la collection Raindrop et la catégorie
/// Miniflux dédiées en une seule opération, appelée par le mode CLI <c>--add-watch-topic</c>. Pas de
/// sur-abstraction (ADR 0001) : les deux appels tiennent dans une seule classe plutôt que deux composants
/// séparés. <see cref="WatchTopic.Id"/> vaut <c>0</c> dans le résultat — non encore persisté, à ignorer côté
/// appelant (<see cref="IWatchTopicRepository.AddAsync"/> génère le vrai id). Le curseur
/// (<see cref="WatchTopic.LastMinifluxEntryId"/>) vaut <c>null</c> ici aussi : c'est l'appelant qui décide de
/// la valeur de seed (voir le mode CLI — une catégorie fraîchement créée est vide, donc sûre à seeder à "0").
/// </summary>
public sealed class WatchTopicProvisioner : IWatchTopicProvisioner
{
    private readonly IRaindropClient _raindropClient;
    private readonly HttpClient _minifluxHttpClient;

    public WatchTopicProvisioner(IRaindropClient raindropClient, HttpClient minifluxHttpClient, IOptions<MinifluxOptions> minifluxOptions)
    {
        _raindropClient = raindropClient;
        _minifluxHttpClient = minifluxHttpClient;

        var options = minifluxOptions.Value;
        _minifluxHttpClient.BaseAddress ??= new Uri(options.BaseUrl.TrimEnd('/') + "/");
        _minifluxHttpClient.DefaultRequestHeaders.Remove("X-Auth-Token");
        _minifluxHttpClient.DefaultRequestHeaders.Add("X-Auth-Token", options.ApiToken);
    }

    public async Task<WatchTopic> ProvisionAsync(string name, string description, CancellationToken cancellationToken)
    {
        var collectionId = await _raindropClient.CreateCollectionAsync(name, cancellationToken);
        var categoryId = await CreateMinifluxCategoryAsync(name, cancellationToken);

        return new WatchTopic(0, name, description, categoryId, collectionId, null, DateTimeOffset.UtcNow);
    }

    private async Task<int> CreateMinifluxCategoryAsync(string title, CancellationToken cancellationToken)
    {
        var response = await _minifluxHttpClient.PostAsJsonAsync("v1/categories", new { title }, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<MinifluxCategoryDto>(cancellationToken: cancellationToken);
        return payload?.Id ?? throw new InvalidOperationException("Réponse Miniflux POST /v1/categories vide ou invalide.");
    }

    private sealed class MinifluxCategoryDto
    {
        public int Id { get; set; }
    }
}
