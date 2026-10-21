using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrionSearch.Domain.Content;
using OrionSearch.Domain.Research;

namespace OrionSearch.Infrastructure.Persistence.Configurations;

public sealed class ArchiveDocumentConfiguration : IEntityTypeConfiguration<ArchiveDocument>
{
    public void Configure(EntityTypeBuilder<ArchiveDocument> builder)
    {
        builder.ToTable("archive_documents", "content", table =>
        {
            table.HasCheckConstraint("ck_archive_documents_relevance", "relevance_score IS NULL OR (relevance_score >= 0 AND relevance_score <= 1)");
        });
        builder.HasKey(x => new { x.ArchiveId, x.DocumentId }).HasName("pk_archive_documents");
        builder.Property(x => x.ArchiveId).HasColumnName("archive_id").HasColumnType("uuid");
        builder.Property(x => x.DocumentId).HasColumnName("document_id").HasColumnType("uuid");
        builder.Property(x => x.RelevanceScore).HasColumnName("relevance_score").HasColumnType("numeric");
        builder.Property(x => x.AddedAt).HasColumnName("added_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("now()");

        builder.HasOne<Archive>().WithMany().HasForeignKey(x => x.ArchiveId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Document>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.DocumentId).HasDatabaseName("idx_archive_documents_document");
    }
}
