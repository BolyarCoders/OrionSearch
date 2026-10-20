using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrionSearch.Domain.Research;

namespace OrionSearch.Infrastructure.Persistence.Configurations;

public sealed class ResearchQueryConfiguration : IEntityTypeConfiguration<ResearchQuery>
{
    public void Configure(EntityTypeBuilder<ResearchQuery> builder)
    {
        builder.ToTable("research_queries", "research", table =>
        {
            table.HasCheckConstraint("ck_research_queries_status", "status IN ('queued', 'running', 'completed', 'failed')");
            table.HasCheckConstraint("ck_research_queries_query", "btrim(query) <> ''");
            table.HasCheckConstraint("ck_research_queries_provider", "btrim(provider) <> ''");
        });
        builder.HasKey(x => x.Id).HasName("pk_research_queries");
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.RunId).HasColumnName("run_id").HasColumnType("uuid");
        builder.Property(x => x.Query).HasColumnName("query").HasColumnType("text").IsRequired();
        builder.Property(x => x.Provider).HasColumnName("provider").HasColumnType("text").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasColumnType("text").IsRequired().HasDefaultValue("queued");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("now()");

        builder.HasOne<ResearchRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.RunId).HasDatabaseName("idx_research_queries_run");
    }
}
