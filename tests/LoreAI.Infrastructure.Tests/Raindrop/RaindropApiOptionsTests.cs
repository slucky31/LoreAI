using LoreAI.Infrastructure.Raindrop;

namespace LoreAI.Infrastructure.Tests.Raindrop;

public class RaindropApiOptionsTests
{
    /// <summary>
    /// Invariant central de l'ADR 0007 : l'outil ne travaille que sur « Non trié » (-1). Tout ce qui est
    /// en dehors est considéré comme déjà rangé par l'utilisateur et ne doit jamais être retouché.
    /// Ce test verrouille le défaut ; c'est sa dérive dans .env.example qui a motivé le finding F-02.
    /// </summary>
    [Fact]
    public void CollectionId_DefaultsToNonTrie()
    {
        var options = new RaindropApiOptions { Token = "peu-importe" };

        Assert.Equal(-1, options.CollectionId);
    }
}
