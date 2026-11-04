using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrionSearch.Domain.Content;

namespace OrionSearch.Infrastructure.Persistence.Configurations;

public sealed class ArchiveDocumentVersionConfiguration : IEntityTypeConfiguration<ArchiveDocumentVersion>
{
    public void Configure(EntityTypeBuilder<ArchiveDocumentVersion> builder)
    {
        builder.ToTable("archive_document_versions", "content", table =>
            table.HasCheckConstraint("ck_archive_versions_raw", "(storage_uri IS NULL AND raw_sha256 IS NULL) OR (storage_uri IS NOT NULL AND btrim(storage_uri) <> '' AND raw_sha256 IS NOT NULL AND raw_sha256 ~ '^[0-9a-f]{64}$')"));
        builder.HasKey(x => new { x.ArchiveId, x.DocumentVersionId }).HasName("pk_archive_document_versions");
        builder.Property(x => x.ArchiveId).HasColumnName("archive_id");
        builder.Property(x => x.DocumentId).HasColumnName("document_id");
        builder.Property(x => x.DocumentVersionId).HasColumnName("document_version_id");
        builder.Property(x => x.StorageUri).HasColumnName("storage_uri");
        builder.Property(x => x.RawSha256).HasColumnName("raw_sha256");
        builder.HasOne<ArchiveDocument>().WithMany().HasForeignKey(x => new { x.ArchiveId, x.DocumentId }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<DocumentVersion>().WithMany().HasForeignKey(x => new { x.DocumentVersionId, x.DocumentId })
            .HasPrincipalKey(x => new { x.Id, x.DocumentId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.DocumentVersionId, x.DocumentId }).HasDatabaseName("idx_archive_versions_version");
    }
}
