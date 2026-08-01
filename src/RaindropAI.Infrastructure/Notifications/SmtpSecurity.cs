namespace RaindropAI.Infrastructure.Notifications;

/// <summary>
/// Mode de sécurisation de la connexion SMTP. Aucune valeur ne permet de transmettre les identifiants
/// en clair : c'est délibéré, un réglage de configuration ne doit pas pouvoir désactiver le chiffrement
/// d'une authentification.
/// </summary>
public enum SmtpSecurity
{
    /// <summary>Déduit du port : 465 → TLS implicite, tout autre port → STARTTLS obligatoire. Défaut.</summary>
    Auto,

    /// <summary>STARTTLS obligatoire (port 587 en général) ; la connexion échoue si le serveur ne l'annonce pas.</summary>
    StartTls,

    /// <summary>TLS implicite dès l'ouverture de la connexion (port 465).</summary>
    SslOnConnect,
}
