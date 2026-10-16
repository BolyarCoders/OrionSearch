using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrionSearch.Domain.Content;

namespace OrionSearch.Infrastructure.Persistence.Configurations;

public sealed class DocumentVersionConfiguration : IEntityTypeConfiguration<DocumentVersion>
{
    public void Configure(EntityTypeBuilder<DocumentVersion> builder)
    {
        builder.ToTable("document_versions", "content", table =>
        {
            table.HasCheckConstraint("ck_document_versions_hash", "content_hash ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_document_versions_metadata", "jsonb_typeof(metadata) = 'object'");
        });
        builder.HasKey(x => x.Id).HasName("pk_document_versions");
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.DocumentId).HasColumnName("document_id").HasColumnType("uuid");
        builder.Property(x => x.ContentHash).HasColumnName("content_hash").HasColumnType("text").IsRequired();
        builder.Property(x => x.ExtractedText).HasColumnName("extracted_text").HasColumnType("text").IsRequired();
        builder.Property(x => x.FetchedAt).HasColumnName("fetched_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("now()");
        builder.Property(x => x.StorageUri).HasColumnName("storage_uri").HasColumnType("text");
        builder.Property(x => x.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired().HasDefaultValueSql("'{}'::jsonb");

        builder.HasOne<Document>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.DocumentId, x.ContentHash }).IsUnique().HasDatabaseName("ux_document_versions_content");
        builder.HasIndex(x => new { x.DocumentId, x.FetchedAt }).IsDescending(false, true).HasDatabaseName("idx_document_versions_document_fetched");
    }
}
