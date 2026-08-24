using NSubstitute;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;
using LoreAI.Mcp.Tools;
using ModelContextProtocol;

namespace LoreAI.Mcp.Tests.Tools;

public class CorpusToolsTests
{
    private readonly ICorpusQueryRepository _repository = Substitute.For<ICorpusQueryRepository>();
    private readonly CorpusTools _tools;

    public CorpusToolsTests()
    {
        _tools = new CorpusTools(_repository);
    }

    [Fact]
    public async Task GetItem_ExistingId_ReturnsRepositoryResult()
    {
        var summary = CreateSummary(1);
        _repository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(summary);

        var result = await _tools.GetItem(1, TestContext.Current.CancellationToken);

        Assert.Equal(summary, result);
    }

    [Fact]
    public async Task GetItem_UnknownId_ThrowsMcpException()
    {
        _repository.GetByIdAsync(999, Arg.Any<CancellationToken>()).Returns((LibraryItemSummary?)null);

        await Assert.ThrowsAsync<McpException>(() => _tools.GetItem(999, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(-5, 20)]
    [InlineData(50, 50)]
    [InlineData(1000, 200)]
    public async Task ListRecent_ClampsCountToValidRange(int requested, int expected)
    {
        _repository.GetRecentAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);

        await _tools.ListRecent(requested, TestContext.Current.CancellationToken);

        await _repository.Received(1).GetRecentAsync(expected, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(-1, 20)]
    [InlineData(500, 100)]
    public async Task SearchItems_ClampsLimitToValidRange(int requested, int expected)
    {
        _repository.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);

        await _tools.SearchItems("dotnet", requested, TestContext.Current.CancellationToken);

        await _repository.Received(1).SearchAsync("dotnet", expected, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(-1, 20)]
    [InlineData(500, 100)]
    public async Task FindSimilar_ClampsLimitToValidRange(int requested, int expected)
    {
        _repository.FindSimilarAsync(Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);

        await _tools.FindSimilar(1, requested, TestContext.Current.CancellationToken);

        await _repository.Received(1).FindSimilarAsync(1, expected, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CatalogTools_ReturnsRepositoryResult()
    {
        var tools = new[] { new ToolSummary(1, "Ollama", "CLI", "À évaluer", null, 2, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow) };
        _repository.GetToolsAsync(Arg.Any<CancellationToken>()).Returns(tools);

        var result = await _tools.CatalogTools(TestContext.Current.CancellationToken);

        Assert.Equal(tools, result);
    }

    [Fact]
    public async Task ToolCard_ExistingName_ReturnsMarkdownWithToolName()
    {
        var card = new ToolCard(1, "Ollama", "CLI", "À évaluer", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, []);
        _repository.GetToolByNameAsync("Ollama", Arg.Any<CancellationToken>()).Returns(card);

        var markdown = await _tools.ToolCard("Ollama", TestContext.Current.CancellationToken);

        Assert.Contains("Ollama", markdown);
    }

    [Fact]
    public async Task ToolCard_UnknownName_ThrowsMcpException()
    {
        _repository.GetToolByNameAsync("Inconnu", Arg.Any<CancellationToken>()).Returns((ToolCard?)null);

        await Assert.ThrowsAsync<McpException>(() => _tools.ToolCard("Inconnu", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExportItem_ExistingId_ReturnsMarkdownWithSummary()
    {
        var summary = CreateSummary(1);
        _repository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(summary);
        _repository.GetArticleSummaryAsync(1, Arg.Any<CancellationToken>()).Returns("Un résumé.");

        var markdown = await _tools.ExportItem(1, TestContext.Current.CancellationToken);

        Assert.Contains(summary.Title, markdown);
        Assert.Contains("Un résumé.", markdown);
    }

    [Fact]
    public async Task ExportItem_NeverClassified_ReturnsMarkdownWithoutSummary()
    {
        var summary = CreateSummary(1);
        _repository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(summary);
        _repository.GetArticleSummaryAsync(1, Arg.Any<CancellationToken>()).Returns((string?)null);

        var markdown = await _tools.ExportItem(1, TestContext.Current.CancellationToken);

        Assert.Contains("pas de résumé disponible", markdown);
    }

    [Fact]
    public async Task ExportItem_UnknownId_ThrowsMcpException()
    {
        _repository.GetByIdAsync(999, Arg.Any<CancellationToken>()).Returns((LibraryItemSummary?)null);

        await Assert.ThrowsAsync<McpException>(() => _tools.ExportItem(999, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Stats_ReturnsRepositoryResult()
    {
        var stats = new CorpusStats(10, 2, 1, DateTimeOffset.UtcNow);
        _repository.GetStatsAsync(Arg.Any<CancellationToken>()).Returns(stats);

        var result = await _tools.Stats(TestContext.Current.CancellationToken);

        Assert.Equal(stats, result);
    }

    [Fact]
    public void ListTools_ListsAllPlannedToolsFromIssue44AndLot5()
    {
        var tools = _tools.ListTools();

        Assert.Equal(
            ["get_item", "list_recent", "search_items", "stats", "list_tools", "find_similar", "catalog_tools", "tool_card", "export_item", "reading_queue"],
            tools.Select(t => t.Name));
    }

    [Fact]
    public void ListTools_ReadingQueue_IsMarkedNotImplemented()
    {
        var tools = _tools.ListTools();

        Assert.Contains(tools, t => t.Name == "reading_queue" && t.Status.StartsWith("non implémenté", StringComparison.Ordinal));
    }

    [Fact]
    public void ListTools_FindSimilar_IsMarkedImplemented()
    {
        var tools = _tools.ListTools();

        Assert.Contains(tools, t => t.Name == "find_similar" && t.Status.StartsWith("implémenté", StringComparison.Ordinal));
    }

    private static LibraryItemSummary CreateSummary(long id) =>
        new(id, "Titre", "https://example.com", [], null, DateTimeOffset.UtcNow);
}
