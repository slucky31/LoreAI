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
            article.HasIndex(a => a.RaindropCreatedUtc);
        });

        modelBuilder.Entity<PollingStateEntity>(pollingState =>
        {
            // Ligne unique par construction (PollingStateRepository force toujours Id = 1).
            pollingState.Property(p => p.Id).ValueGeneratedNever();
            pollingState.ToTable(t => t.HasCheckConstraint("CK_PollingState_SingleRow", "\"Id\" = 1"));
        });
    }
}
