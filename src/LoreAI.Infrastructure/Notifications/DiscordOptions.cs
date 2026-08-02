using System.ComponentModel.DataAnnotations;

namespace LoreAI.Infrastructure.Notifications;

public sealed class DiscordOptions
{
    [Required(AllowEmptyStrings = false)]
    [Url]
    public required string WebhookUrl { get; init; }
}
