using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrionSearch.Domain.Content;

namespace OrionSearch.Infrastructure.Persistence.Configurations;

public sealed class DocumentChunkConfiguration : IEntityTypeConfiguration<DocumentChunk>
{
    public void Configure(EntityTypeBuilder<DocumentChunk> builder)
    {
        builder.ToTable("document_chunks", "content", table =>
        {
            table.HasCheckConstraint("ck_document_chunks_index", "chunk_index >= 0");
            table.HasCheckConstraint("ck_document_chunks_tokens", "token_count IS NULL OR token_count >= 0");
            table.HasCheckConstraint("ck_document_chunks_offsets", "(start_offset IS NULL AND end_offset IS NULL) OR (start_offset IS NOT NULL AND end_offset IS NOT NULL AND start_offset >= 0 AND end_offset > start_offset)");
        });
        builder.HasKey(x => x.Id).HasName("pk_document_chunks");
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.VersionId).HasColumnName("version_id").HasColumnType("uuid");
        builder.Property(x => x.ChunkIndex).HasColumnName("chunk_index").HasColumnType("integer");
        builder.Property(x => x.Content).HasColumnName("content").HasColumnType("text").IsRequired();
        builder.Property(x => x.TokenCount).HasColumnName("token_count").HasColumnType("integer");
        builder.Property(x => x.StartOffset).HasColumnName("start_offset").HasColumnType("integer");
        builder.Property(x => x.EndOffset).HasColumnName("end_offset").HasColumnType("integer");

        builder.HasOne<DocumentVersion>().WithMany().HasForeignKey(x => x.VersionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasAlternateKey(x => new { x.Id, x.VersionId }).HasName("ak_document_chunks_id_version");
        builder.HasIndex(x => new { x.VersionId, x.ChunkIndex }).IsUnique().HasDatabaseName("ux_document_chunks_version_index");
        builder.HasIndex(x => x.Content).HasMethod("GIN").IsTsVectorExpressionIndex("simple").HasDatabaseName("idx_chunks_fts");
    }
}
