using MailKit.Security;

namespace LoreAI.Infrastructure.Notifications;

/// <summary>
/// Traduit <see cref="SmtpSecurity"/> en options MailKit. Pure, testable sans serveur SMTP.
/// <para>
/// Deux valeurs MailKit sont volontairement inatteignables : <see cref="SecureSocketOptions.None"/>, qui
/// transmettrait les identifiants en clair, et <see cref="SecureSocketOptions.Auto"/>, qui y retombe
/// silencieusement quand le serveur n'annonce pas STARTTLS. Le mode <see cref="SmtpSecurity.Auto"/> choisit
/// donc lui-même selon le port, entre deux modes qui échouent plutôt que de dégrader la connexion.
/// </para>
/// </summary>
public static class SmtpSecurityResolver
{
    private const int ImplicitTlsPort = 465;

    public static SecureSocketOptions Resolve(SmtpSecurity security, int port) => security switch
    {
        SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
        SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        _ => port == ImplicitTlsPort ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls,
    };
}
