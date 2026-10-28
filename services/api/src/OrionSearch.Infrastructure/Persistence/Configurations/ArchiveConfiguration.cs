using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrionSearch.Domain.Auth;
using OrionSearch.Domain.Research;

namespace OrionSearch.Infrastructure.Persistence.Configurations;

public sealed class ArchiveConfiguration : IEntityTypeConfiguration<Archive>
{
    public void Configure(EntityTypeBuilder<Archive> builder)
    {
        builder.ToTable("archives", "research", table =>
        {
            table.HasCheckConstraint("ck_archives_status", "status IN ('active', 'paused', 'archived')");
            table.HasCheckConstraint("ck_archives_title", "btrim(title) <> ''");
        });
        builder.HasKey(x => x.Id).HasName("pk_archives");
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.OwnerId).HasColumnName("owner_id").HasColumnType("uuid");
        builder.Property(x => x.Title).HasColumnName("title").HasColumnType("text").IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasColumnType("text");
        builder.Property(x => x.Status).HasColumnName("status").HasColumnType("text").IsRequired().HasDefaultValue("active");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();

        builder.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.OwnerId).HasDatabaseName("idx_archives_owner");
    }
}
