using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrionSearch.Domain.Knowledge;
using OrionSearch.Domain.Research;

namespace OrionSearch.Infrastructure.Persistence.Configurations;

public sealed class FindingConfiguration : IEntityTypeConfiguration<Finding>
{
    public void Configure(EntityTypeBuilder<Finding> builder)
    {
        builder.ToTable("findings", "knowledge", table =>
        {
            table.HasCheckConstraint("ck_findings_statement", "btrim(statement) <> ''");
            table.HasCheckConstraint("ck_findings_type", "finding_type IN ('claim', 'observation', 'estimate')");
            table.HasCheckConstraint("ck_findings_status", "status IN ('unverified', 'supported', 'disputed', 'superseded')");
            table.HasCheckConstraint("ck_findings_confidence", "confidence IS NULL OR (confidence >= 0 AND confidence <= 1)");
        });
        builder.HasKey(x => x.Id).HasName("pk_findings");
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.ResearchRunId).HasColumnName("research_run_id").HasColumnType("uuid");
        builder.HasOne<ResearchRun>().WithMany().HasForeignKey(x => x.ResearchRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.ResearchRunId).HasDatabaseName("idx_findings_research_run");
        builder.Property(x => x.Statement).HasColumnName("statement").HasColumnType("text").IsRequired();
        builder.Property(x => x.FindingType).HasColumnName("finding_type").HasColumnType("text").IsRequired().HasDefaultValue("claim");
        builder.Property(x => x.Status).HasColumnName("status").HasColumnType("text").IsRequired().HasDefaultValue("unverified");
        builder.Property(x => x.Confidence).HasColumnName("confidence").HasColumnType("numeric");
        builder.Property(x => x.FirstObservedAt).HasColumnName("first_observed_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("now()");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("now()");

        builder.HasIndex(x => x.FirstObservedAt).IsDescending().HasDatabaseName("idx_findings_first_observed");
    }
}
