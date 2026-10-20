using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrionSearch.Domain.Content;
using OrionSearch.Domain.Knowledge;

namespace OrionSearch.Infrastructure.Persistence.Configurations;

public sealed class FindingEvidenceConfiguration : IEntityTypeConfiguration<FindingEvidence>
{
    public void Configure(EntityTypeBuilder<FindingEvidence> builder)
    {
        builder.ToTable("finding_evidence", "knowledge", table =>
        {
            table.HasCheckConstraint("ck_finding_evidence_stance", "stance IN ('supports', 'contradicts', 'mentions')");
        });
        builder.HasKey(x => x.Id).HasName("pk_finding_evidence");
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.FindingId).HasColumnName("finding_id").HasColumnType("uuid");
        builder.Property(x => x.DocumentVersionId).HasColumnName("document_version_id").HasColumnType("uuid");
        builder.Property(x => x.ChunkId).HasColumnName("chunk_id").HasColumnType("uuid");
        builder.Property(x => x.EvidenceText).HasColumnName("evidence_text").HasColumnType("text");
        builder.Property(x => x.Stance).HasColumnName("stance").HasColumnType("text").IsRequired().HasDefaultValue("mentions");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("now()");

        builder.HasOne<Finding>().WithMany().HasForeignKey(x => x.FindingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DocumentVersion>().WithMany().HasForeignKey(x => x.DocumentVersionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DocumentChunk>().WithMany()
            .HasForeignKey(x => new { x.ChunkId, x.DocumentVersionId })
            .HasPrincipalKey(x => new { x.Id, x.VersionId })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_finding_evidence_chunk_version");
        builder.HasIndex(x => x.FindingId).HasDatabaseName("idx_finding_evidence_finding");
        builder.HasIndex(x => x.DocumentVersionId).HasDatabaseName("idx_finding_evidence_version");
        builder.HasIndex(x => new { x.ChunkId, x.DocumentVersionId }).HasDatabaseName("idx_finding_evidence_chunk_version");
    }
}
