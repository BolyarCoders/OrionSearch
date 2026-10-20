using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrionSearch.Domain.Content;

namespace OrionSearch.Infrastructure.Persistence.Configurations;

public sealed class DocumentVersionConfiguration : IEntityTypeConfiguration<DocumentVersion>
{
    private const string WhitespaceSql = """E' \t\n\r\f' || U&'\000B\0085\00A0\1680\2000\2001\2002\2003\2004\2005\2006\2007\2008\2009\200A\2028\2029\202F\205F\3000'""";
    public void Configure(EntityTypeBuilder<DocumentVersion> builder)
    {
        builder.ToTable("document_versions", "content", table =>
        {
            table.HasCheckConstraint("ck_document_versions_hash", "content_hash ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_document_versions_metadata", "jsonb_typeof(metadata) = 'object'");
            table.HasCheckConstraint("ck_document_versions_content_presence", "extracted_text IS NOT NULL OR storage_uri IS NOT NULL");
            table.HasCheckConstraint("ck_document_versions_text_not_blank", $"extracted_text IS NULL OR btrim(extracted_text, {WhitespaceSql}) <> ''");
            table.HasCheckConstraint("ck_document_versions_storage_not_blank", $"storage_uri IS NULL OR btrim(storage_uri, {WhitespaceSql}) <> ''");
            table.HasCheckConstraint("ck_document_versions_raw_hash_metadata",
                "extracted_text IS NOT NULL OR COALESCE(metadata #>> '{content_hash,algorithm}' = 'sha256' AND metadata #>> '{content_hash,basis}' = 'raw_bytes', false)");
        });
        builder.HasKey(x => x.Id).HasName("pk_document_versions");
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.DocumentId).HasColumnName("document_id").HasColumnType("uuid");
        builder.Property(x => x.ContentHash).HasColumnName("content_hash").HasColumnType("text").IsRequired();
        builder.Property(x => x.ExtractedText).HasColumnName("extracted_text").HasColumnType("text");
        builder.Property(x => x.FetchedAt).HasColumnName("fetched_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("now()");
        builder.Property(x => x.StorageUri).HasColumnName("storage_uri").HasColumnType("text");
        builder.Property(x => x.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired().HasDefaultValueSql("'{}'::jsonb");

        builder.HasOne<Document>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.DocumentId, x.ContentHash }).IsUnique().HasDatabaseName("ux_document_versions_content");
        builder.HasIndex(x => new { x.DocumentId, x.FetchedAt }).IsDescending(false, true).HasDatabaseName("idx_document_versions_document_fetched");
    }
}
