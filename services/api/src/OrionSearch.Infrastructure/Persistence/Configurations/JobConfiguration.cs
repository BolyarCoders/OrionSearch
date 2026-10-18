using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrionSearch.Domain.Jobs;
using OrionSearch.Domain.Research;

namespace OrionSearch.Infrastructure.Persistence.Configurations;

public sealed class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> builder)
    {
        builder.ToTable("jobs", "system", table =>
        {
            table.HasCheckConstraint("ck_jobs_status", "status IN ('pending', 'processing', 'completed', 'failed', 'cancelled')");
            table.HasCheckConstraint("ck_jobs_type", "job_type IN ('research_plan', 'web_search', 'fetch_document', 'extract_content', 'analyze_document', 'generate_embeddings', 'update_knowledge', 'generate_magazine')");
            table.HasCheckConstraint("ck_jobs_attempts", "attempts >= 0 AND max_attempts > 0 AND attempts <= max_attempts");
            table.HasCheckConstraint("ck_jobs_payload", "jsonb_typeof(payload) = 'object'");
            table.HasCheckConstraint("ck_jobs_completed", "completed_at IS NULL OR completed_at >= created_at");
            table.HasCheckConstraint("ck_jobs_idempotency", "idempotency_key IS NULL OR btrim(idempotency_key) <> ''");
        });
        builder.HasKey(x => x.Id).HasName("pk_jobs");
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.ResearchRunId).HasColumnName("research_run_id").HasColumnType("uuid");
        builder.Property(x => x.JobType).HasColumnName("job_type").HasColumnType("text").IsRequired();
        builder.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired().HasDefaultValueSql("'{}'::jsonb");
        builder.Property(x => x.Status).HasColumnName("status").HasColumnType("text").IsRequired().HasDefaultValue("pending");
        builder.Property(x => x.Priority).HasColumnName("priority").HasColumnType("integer").HasDefaultValue(0);
        builder.Property(x => x.Attempts).HasColumnName("attempts").HasColumnType("integer").HasDefaultValue(0);
        builder.Property(x => x.MaxAttempts).HasColumnName("max_attempts").HasColumnType("integer").HasDefaultValue(3).ValueGeneratedNever();
        builder.Property(x => x.RunAfter).HasColumnName("run_after").HasColumnType("timestamp with time zone").HasDefaultValueSql("now()");
        builder.Property(x => x.LockedUntil).HasColumnName("locked_until").HasColumnType("timestamp with time zone");
        builder.Property(x => x.WorkerId).HasColumnName("worker_id").HasColumnType("text");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasColumnType("text");
        builder.Property(x => x.LastError).HasColumnName("last_error").HasColumnType("text");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at").HasColumnType("timestamp with time zone");

        builder.HasOne<ResearchRun>().WithMany().HasForeignKey(x => x.ResearchRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.ResearchRunId).HasDatabaseName("idx_jobs_research_run");
        builder.HasIndex(x => new { x.Priority, x.CreatedAt }).IsDescending(true, false).HasFilter("status = 'pending'").HasDatabaseName("idx_jobs_ready");
        builder.HasIndex(x => x.RunAfter).HasFilter("status = 'pending'").HasDatabaseName("idx_jobs_run_after");
        builder.HasIndex(x => x.LockedUntil).HasFilter("status = 'processing'").HasDatabaseName("idx_jobs_expired_leases");
        builder.HasIndex(x => x.IdempotencyKey).IsUnique().HasFilter("idempotency_key IS NOT NULL").HasDatabaseName("ux_jobs_idempotency");
        builder.HasIndex(x => x.ResearchRunId, "ResearchPlanRun").IsUnique()
            .HasFilter("research_run_id IS NOT NULL AND job_type = 'research_plan'").HasDatabaseName("ux_jobs_research_plan_run");
    }
}
