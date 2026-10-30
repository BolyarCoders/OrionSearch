using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrionSearch.Domain.Research;

namespace OrionSearch.Infrastructure.Persistence.Configurations;

public sealed class ResearchRunConfiguration : IEntityTypeConfiguration<ResearchRun>
{
    public void Configure(EntityTypeBuilder<ResearchRun> builder)
    {
        builder.ToTable("research_runs", "research", table =>
        {
            table.HasCheckConstraint("ck_research_runs_status", "status IN ('queued', 'running', 'completed', 'failed', 'cancelled')");
            table.HasCheckConstraint("ck_research_runs_trigger", "trigger_type IN ('manual', 'scheduled', 'system')");
            table.HasCheckConstraint("ck_research_runs_times", "started_at IS NULL OR finished_at IS NULL OR finished_at >= started_at");
            table.HasCheckConstraint("ck_research_runs_stats", "jsonb_typeof(stats) = 'object'");
            table.HasCheckConstraint("ck_research_runs_report", "report IS NULL OR (char_length(report) BETWEEN 1 AND 100000 AND btrim(report) <> '')");
        });
        builder.HasKey(x => x.Id).HasName("pk_research_runs");
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.ArchiveId).HasColumnName("archive_id").HasColumnType("uuid");
        builder.Property(x => x.Status).HasColumnName("status").HasColumnType("text").IsRequired().HasDefaultValue("queued");
        builder.Property(x => x.TriggerType).HasColumnName("trigger_type").HasColumnType("text").IsRequired().HasDefaultValue("manual");
        builder.Property(x => x.StartedAt).HasColumnName("started_at").HasColumnType("timestamp with time zone");
        builder.Property(x => x.FinishedAt).HasColumnName("finished_at").HasColumnType("timestamp with time zone");
        builder.Property(x => x.Stats).HasColumnName("stats").HasColumnType("jsonb").IsRequired().HasDefaultValueSql("'{}'::jsonb");
        builder.Property(x => x.ErrorMessage).HasColumnName("error_message").HasColumnType("text");
        builder.Property(x => x.Report).HasColumnName("report").HasColumnType("text");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("now()");

        builder.HasOne<Archive>().WithMany().HasForeignKey(x => x.ArchiveId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.ArchiveId, x.CreatedAt }).IsDescending(false, true).HasDatabaseName("idx_research_runs_archive_created");
        builder.HasIndex(x => x.CreatedAt).HasDatabaseName("idx_research_runs_created_budget");
    }
}
