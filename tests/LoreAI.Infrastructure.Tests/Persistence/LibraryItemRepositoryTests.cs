using System.Globalization;
using Microsoft.EntityFrameworkCore;
using LoreAI.Core.Enums;
using LoreAI.Core.Models;
using LoreAI.Infrastructure.Persistence;

namespace LoreAI.Infrastructure.Tests.Persistence;

[Collection("Postgres")]
public class LibraryItemRepositoryTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly LibraryItemRepository _repository;

    public LibraryItemRepositoryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _repository = new LibraryItemRepository(fixture.ContextFactory, new PostgresSchemaGuard(fixture.ContextFactory));
    }

    public async ValueTask InitializeAsync()
    {
        await using var context = _fixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"LibraryItems\" RESTART IDENTITY CASCADE");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task UpsertPageAsync_CalledTwiceWithSameId_DoesNotDuplicate()
    {
        var item = CreateLibraryItem(1, "Titre initial");

        await _repository.UpsertPageAsync([item], DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
        await _repository.UpsertPageAsync(
            [item with { Item = item.Item with { Title = "Titre mis à jour" } }],
            DateTimeOffset.UtcNow,
            TestContext.Current.CancellationToken);

        await using var context = _fixture.CreateContext();
        var all = await context.LibraryItems.ToListAsync(TestContext.Current.CancellationToken);

        var single = Assert.Single(all);
        Assert.Equal("Titre mis à jour", single.Title);
    }

    [Fact]
    public async Task UpsertPageAsync_MixedNewAndExistingIdsInSamePage_UpsertsBoth()
    {
        await _repository.UpsertPageAsync([CreateLibraryItem(1, "A")], DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        await _repository.UpsertPageAsync(
            [CreateLibraryItem(1, "A mis à jour"), CreateLibraryItem(2, "B")],
            DateTimeOffset.UtcNow,
            TestContext.Current.CancellationToken);

        await using var context = _fixture.CreateContext();
        var all = await context.LibraryItems.OrderBy(e => e.Id).ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, all.Count);
        Assert.Equal("A mis à jour", all[0].Title);
        Assert.Equal("B", all[1].Title);
    }

    [Fact]
    public async Task UpsertPageAsync_RoundTripsOriginAndRaindropFields()
    {
        var item = CreateLibraryItem(1, "A") with
        {
            Origin = ItemOrigin.Unsorted,
            RaindropCollectionId = -1,
            Broken = true,
            Important = true,
            Cover = "https://example.com/cover.png",
            HighlightsJson = """[{"text":"quote"}]""",
        };

        await _repository.UpsertPageAsync([item], DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        await using var context = _fixture.CreateContext();
        var entity = await context.LibraryItems.SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Unsorted", entity.Origin);
        Assert.Equal(-1, entity.RaindropCollectionId);
        Assert.True(entity.Broken);
        Assert.True(entity.Important);
        Assert.Equal("https://example.com/cover.png", entity.Cover);
        Assert.NotNull(entity.HighlightsJson);
        Assert.Contains("quote", entity.HighlightsJson);
    }

    /// <summary>Une page par appel, un seul <c>SaveChangesAsync</c> chacune — valide le pattern à l'échelle visée par le lot 1 (#42).</summary>
    [Fact]
    public async Task UpsertPageAsync_WithManyItemsAcrossSeveralPages_PersistsAllOfThem()
    {
        const int count = 500;
        const int pageSize = 50;

        for (var start = 1; start <= count; start += pageSize)
        {
            var page = Enumerable.Range(start, pageSize).Select(id => CreateLibraryItem(id, $"A{id}")).ToList();
            await _repository.UpsertPageAsync(page, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
        }

        await using var context = _fixture.CreateContext();
        Assert.Equal(count, await context.LibraryItems.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpsertPageAsync_EmptyPage_DoesNothing()
    {
        await _repository.UpsertPageAsync([], DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        await using var context = _fixture.CreateContext();
        Assert.Empty(await context.LibraryItems.ToListAsync(TestContext.Current.CancellationToken));
    }

    private static LibraryItem CreateLibraryItem(long id, string title) => new(
        new Item(SourceType.Raindrop, id.ToString(CultureInfo.InvariantCulture), "https://example.com", title, "extrait", "note", [], DateTimeOffset.UtcNow),
        ItemOrigin.Library,
        null,
        false,
        false,
        null,
        null);
}
