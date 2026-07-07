namespace RaindropAI.Infrastructure.Notifications;

public sealed class EmailOptions
{
    public required string SmtpHost { get; init; }
    public int SmtpPort { get; init; } = 587;
    public required string SmtpUser { get; init; }
    public required string SmtpPassword { get; init; }
    public required string FromAddress { get; init; }
    public required string ToAddress { get; init; }
    public bool UseSsl { get; init; } = true;
}
