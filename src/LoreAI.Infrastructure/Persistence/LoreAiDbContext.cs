using Microsoft.EntityFrameworkCore;

namespace LoreAI.Infrastructure.Persistence;

public sealed class LoreAiDbContext(DbContextOptions<LoreAiDbContext> options) : DbContext(options)
{
    public DbSet<ArticleEntity> Articles => Set<ArticleEntity>();
    public DbSet<PollingStateEntity> PollingStates => Set<PollingStateEntity>();
    public DbSet<CycleRunEntity> CycleRuns => Set<CycleRunEntity>();
    public DbSet<LibraryItemEntity> LibraryItems => Set<LibraryItemEntity>();
    public DbSet<LibraryIndexStateEntity> LibraryIndexStates => Set<LibraryIndexStateEntity>();
    public DbSet<ToolEntity> Tools => Set<ToolEntity>();
    public DbSet<EmailExtractionLogEntity> EmailExtractionLogs => Set<EmailExtractionLogEntity>();
    public DbSet<WatchEvaluationLogEntity> WatchEvaluationLogs => Set<WatchEvaluationLogEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ArticleEntity>(article =>
        {
            // Id est généré par la base depuis le lot 8 (#49) : la clé applicative est (SourceType, SourceId),
            // seule commune à toutes les sources (un lien Newsletter n'a pas d'id Raindrop numérique).
            article.Property(a => a.SourceType).HasConversion<string>();
            article.Property(a => a.ClassificationRawResponse).HasColumnType("jsonb");
            article.Property(a => a.RecommendedAction).HasConversion<string>();
            article.Property(a => a.Priority).HasConversion<string>();
            article.Property(a => a.ContentStatus).HasConversion<string>();
            article.Property(a => a.LinkStatus).HasConversion<string>();
            article.HasIndex(a => a.CapturedAtUtc);
            article.HasIndex(a => new { a.SourceType, a.SourceId }).IsUnique();
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

            // Q2 (lot 5) : recherche plein texte française sur titre + extrait, indexée GIN. Remplace
            // l'ILIKE naïf de CorpusQueryRepository.SearchAsync.
            libraryItem.HasGeneratedTsVectorColumn(i => i.SearchVector, "french", i => new { i.Title, i.Excerpt });
            libraryItem.HasIndex(i => i.SearchVector).HasMethod("GIN");
        });

        modelBuilder.Entity<LibraryIndexStateEntity>(libraryIndexState =>
        {
            libraryIndexState.HasKey(s => s.SourceType);
        });

        modelBuilder.Entity<ToolEntity>(tool =>
        {
            // Id généré : contrairement à ArticleEntity/LibraryItemEntity, un outil n'a pas d'identifiant
            // Raindrop naturel (un même outil peut être rencontré via plusieurs articles).
            tool.HasIndex(t => t.Name);
        });

        modelBuilder.Entity<EmailExtractionLogEntity>(extractionLog =>
        {
            // Pas de clé applicative (lot 8, #49), comme CycleRunEntity : rien n'identifie un appel a priori.
            extractionLog.Property(e => e.RawResponse).HasColumnType("jsonb");
            extractionLog.HasIndex(e => e.ProcessedAtUtc);
        });

        modelBuilder.Entity<WatchEvaluationLogEntity>(watchEvaluationLog =>
        {
            // Même patron qu'EmailExtractionLogEntity (lot 9, #50) : pas de clé applicative.
            watchEvaluationLog.Property(e => e.RawResponse).HasColumnType("jsonb");
            watchEvaluationLog.HasIndex(e => e.ProcessedAtUtc);
        });
    }
}
