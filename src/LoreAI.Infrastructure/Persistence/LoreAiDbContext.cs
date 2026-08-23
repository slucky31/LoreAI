using Microsoft.EntityFrameworkCore;

namespace LoreAI.Infrastructure.Persistence;

public sealed class LoreAiDbContext(DbContextOptions<LoreAiDbContext> options) : DbContext(options)
{
    public DbSet<ArticleEntity> Articles => Set<ArticleEntity>();
    public DbSet<PollingStateEntity> PollingStates => Set<PollingStateEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ArticleEntity>(article =>
        {
            // Id est l'identifiant Raindrop, jamais généré côté base : UpsertAsync le fournit toujours.
            article.Property(a => a.Id).ValueGeneratedNever();
            article.Property(a => a.ClassificationRawResponse).HasColumnType("jsonb");
            article.Property(a => a.RecommendedAction).HasConversion<string>();
            article.Property(a => a.Priority).HasConversion<string>();
            article.HasIndex(a => a.EmailDigestSentAtUtc);
            article.HasIndex(a => a.CapturedAtUtc);
        });

        modelBuilder.Entity<PollingStateEntity>(pollingState =>
        {
            // Une ligne par source (ADR 0012) : la clé naturelle SourceType remplace la ligne unique Id = 1.
            pollingState.HasKey(p => p.SourceType);
        });
    }
}
