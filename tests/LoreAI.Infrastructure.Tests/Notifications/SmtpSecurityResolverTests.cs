using MailKit.Security;
using LoreAI.Infrastructure.Notifications;

namespace LoreAI.Infrastructure.Tests.Notifications;

public class SmtpSecurityResolverTests
{
    [Theory]
    [InlineData(587)]
    [InlineData(25)]
    [InlineData(2525)]
    public void Resolve_Auto_UsesMandatoryStartTlsOnNonImplicitPorts(int port)
    {
        Assert.Equal(SecureSocketOptions.StartTls, SmtpSecurityResolver.Resolve(SmtpSecurity.Auto, port));
    }

    [Fact]
    public void Resolve_Auto_UsesImplicitTlsOnPort465()
    {
        Assert.Equal(SecureSocketOptions.SslOnConnect, SmtpSecurityResolver.Resolve(SmtpSecurity.Auto, 465));
    }

    [Fact]
    public void Resolve_ExplicitModes_AreHonouredRegardlessOfPort()
    {
        Assert.Equal(SecureSocketOptions.StartTls, SmtpSecurityResolver.Resolve(SmtpSecurity.StartTls, 465));
        Assert.Equal(SecureSocketOptions.SslOnConnect, SmtpSecurityResolver.Resolve(SmtpSecurity.SslOnConnect, 587));
    }

    /// <summary>
    /// Le cœur du finding F-09 : aucune combinaison ne doit produire une connexion susceptible de
    /// transmettre les identifiants SMTP en clair. `None` transmet en clair, `Auto` (celui de MailKit)
    /// y retombe silencieusement si le serveur n'annonce pas STARTTLS.
    /// </summary>
    [Fact]
    public void Resolve_NoCombination_EverAllowsAnUnencryptedConnection()
    {
        var ports = new[] { 25, 465, 587, 2525, 0 };

        var resolved = from security in Enum.GetValues<SmtpSecurity>()
                       from port in ports
                       select SmtpSecurityResolver.Resolve(security, port);

        Assert.All(resolved, options =>
        {
            Assert.NotEqual(SecureSocketOptions.None, options);
            Assert.NotEqual(SecureSocketOptions.Auto, options);
            Assert.NotEqual(SecureSocketOptions.StartTlsWhenAvailable, options);
        });
    }
}
