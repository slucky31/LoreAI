using Microsoft.EntityFrameworkCore;

namespace LoreAI.Infrastructure.Persistence;

public sealed class LoreAiDbContext(DbContextOptions<LoreAiDbContext> options) : DbContext(options)
{
    public DbSet<ArticleEntity> Articles => Set<ArticleEntity>();
    public DbSet<PollingStateEntity> PollingStates => Set<PollingStateEntity>();
    public DbSet<CycleRunEntity> CycleRuns => Set<CycleRunEntity>();
    public DbSet<LibraryItemEntity> LibraryItems => Set<LibraryItemEntity>();
    public DbSet<LibraryIndexStateEntity> LibraryIndexStates => Set<LibraryIndexStateEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ArticleEntity>(article =>
        {
            // Id est l'identifiant Raindrop, jamais généré côté base : UpsertAsync le fournit toujours.
            article.Property(a => a.Id).ValueGeneratedNever();
            article.Property(a => a.ClassificationRawResponse).HasColumnType("jsonb");
            article.Property(a => a.RecommendedAction).HasConversion<string>();
            article.Property(a => a.Priority).HasConversion<string>();
            article.HasIndex(a => a.CapturedAtUtc);
        });

        modelBuilder.Entity<PollingStateEntity>(pollingState =>
        {
            // Une ligne par source (ADR 0012) : la clé naturelle SourceType remplace la ligne unique Id = 1.
            pollingState.HasKey(p => p.SourceType);
        });

        modelBuilder.Entity<CycleRunEntity>(cycleRun =>
        {
            // Pas de clé applicative ici (contrairement à ArticleEntity/PollingStateEntity) : Id est généré.
            cycleRun.Property(c => c.Outcome).HasConversion<string>();
            cycleRun.HasIndex(c => c.CompletedUtc);
        });

        modelBuilder.Entity<LibraryItemEntity>(libraryItem =>
        {
            // Même convention qu'ArticleEntity : Id est l'identifiant Raindrop, jamais généré côté base.
            libraryItem.Property(i => i.Id).ValueGeneratedNever();
            libraryItem.Property(i => i.HighlightsJson).HasColumnType("jsonb");
            libraryItem.HasIndex(i => i.Origin);
        });

        modelBuilder.Entity<LibraryIndexStateEntity>(libraryIndexState =>
        {
            libraryIndexState.HasKey(s => s.SourceType);
        });
    }
}
