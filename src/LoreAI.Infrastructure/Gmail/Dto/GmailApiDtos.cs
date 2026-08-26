using System.Text.Json.Serialization;

namespace LoreAI.Infrastructure.Gmail.Dto;

/// <summary>Réponse OAuth2 standard (snake_case, RFC 6749) — seul endpoint de ce fichier qui n'est pas l'API Gmail elle-même (camelCase).</summary>
internal sealed class TokenResponseDto
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;
}

internal sealed class LabelsListDto
{
    public List<LabelDto> Labels { get; set; } = [];
}

internal sealed class LabelDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

internal sealed class HistoryListDto
{
    public List<HistoryRecordDto> History { get; set; } = [];
    public string? NextPageToken { get; set; }

    /// <summary>ID de l'état courant de la boîte mail au moment de la requête — c'est ce champ, pas un id de message, qui devient le prochain curseur (cf. GmailIngester).</summary>
    public string? HistoryId { get; set; }
}

internal sealed class HistoryRecordDto
{
    public List<MessageAddedDto> MessagesAdded { get; set; } = [];
}

internal sealed class MessageAddedDto
{
    public MessageRefDto Message { get; set; } = new();
}

internal sealed class MessageRefDto
{
    public string Id { get; set; } = string.Empty;
}

internal sealed class MessageDto
{
    public string Id { get; set; } = string.Empty;

    /// <summary>Millisecondes depuis epoch, en texte (convention Gmail pour les entiers 64 bits en JSON).</summary>
    public string InternalDate { get; set; } = "0";

    public System.Text.Json.JsonElement Payload { get; set; }
}
