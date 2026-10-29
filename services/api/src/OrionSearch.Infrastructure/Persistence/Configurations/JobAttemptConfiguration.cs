using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrionSearch.Domain.Jobs;

namespace OrionSearch.Infrastructure.Persistence.Configurations;

public sealed class JobAttemptConfiguration : IEntityTypeConfiguration<JobAttempt>
{
    public void Configure(EntityTypeBuilder<JobAttempt> builder)
    {
        builder.ToTable("job_attempts", "system", table =>
        {
            table.HasCheckConstraint("ck_job_attempts_number", "attempt_number > 0");
            table.HasCheckConstraint("ck_job_attempts_status", "status IN ('running', 'completed', 'failed', 'timed_out')");
            table.HasCheckConstraint("ck_job_attempts_times", "finished_at IS NULL OR finished_at >= started_at");
        });
        builder.HasKey(x => x.Id).HasName("pk_job_attempts");
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.JobId).HasColumnName("job_id").HasColumnType("uuid");
        builder.Property(x => x.AttemptNumber).HasColumnName("attempt_number").HasColumnType("integer");
        builder.Property(x => x.StartedAt).HasColumnName("started_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("now()");
        builder.Property(x => x.FinishedAt).HasColumnName("finished_at").HasColumnType("timestamp with time zone");
        builder.Property(x => x.Status).HasColumnName("status").HasColumnType("text").IsRequired().HasDefaultValue("running");
        builder.Property(x => x.ErrorMessage).HasColumnName("error_message").HasColumnType("text");

        builder.HasOne<Job>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.JobId, x.AttemptNumber }).IsUnique().HasDatabaseName("ux_job_attempts_number");
        builder.HasIndex(x => x.StartedAt).HasDatabaseName("idx_job_attempts_started_budget");
    }
}
