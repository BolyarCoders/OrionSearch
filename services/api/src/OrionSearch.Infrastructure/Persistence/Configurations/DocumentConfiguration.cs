using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrionSearch.Domain.Content;

namespace OrionSearch.Infrastructure.Persistence.Configurations;

public sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("documents", "content", table =>
        {
            table.HasCheckConstraint("ck_documents_url", "canonical_url ~ '^https?://[^[:space:]]+$'");
            table.HasCheckConstraint("ck_documents_seen", "last_seen_at >= first_seen_at");
        });
        builder.HasKey(x => x.Id).HasName("pk_documents");
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.PublisherId).HasColumnName("publisher_id").HasColumnType("uuid");
        builder.Property(x => x.CanonicalUrl).HasColumnName("canonical_url").HasColumnType("text").IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasColumnType("text");
        builder.Property(x => x.Language).HasColumnName("language").HasColumnType("text");
        builder.Property(x => x.PublishedAt).HasColumnName("published_at").HasColumnType("timestamp with time zone");
        builder.Property(x => x.FirstSeenAt).HasColumnName("first_seen_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("now()");
        builder.Property(x => x.LastSeenAt).HasColumnName("last_seen_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("now()");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();

        builder.Property(x => x.CanonicalUrl).HasConversion(value => CanonicalUrl.Normalize(value), value => value);
        builder.HasOne<Publisher>().WithMany().HasForeignKey(x => x.PublisherId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.CanonicalUrl).IsUnique().HasDatabaseName("ux_documents_canonical_url");
        builder.HasIndex(x => x.PublisherId).HasDatabaseName("idx_documents_publisher");
    }
}
