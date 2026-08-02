using System.ComponentModel.DataAnnotations;

namespace LoreAI.Infrastructure.Notifications;

public sealed class EmailOptions
{
    [Required(AllowEmptyStrings = false)]
    public required string SmtpHost { get; init; }

    [Range(1, 65535)]
    public int SmtpPort { get; init; } = 587;

    [Required(AllowEmptyStrings = false)]
    public required string SmtpUser { get; init; }

    [Required(AllowEmptyStrings = false)]
    public required string SmtpPassword { get; init; }

    [Required(AllowEmptyStrings = false)]
    [EmailAddress]
    public required string FromAddress { get; init; }

    [Required(AllowEmptyStrings = false)]
    [EmailAddress]
    public required string ToAddress { get; init; }

    /// <summary>
    /// Remplace l'ancien booléen <c>UseSsl</c>, qui laissait passer une connexion en clair et dont le nom
    /// suggérait à tort du TLS implicite alors que le code faisait du STARTTLS.
    /// </summary>
    public SmtpSecurity Security { get; init; } = SmtpSecurity.Auto;
}
