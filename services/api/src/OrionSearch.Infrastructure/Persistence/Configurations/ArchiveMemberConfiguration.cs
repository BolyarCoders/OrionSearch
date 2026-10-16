using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrionSearch.Domain.Auth;
using OrionSearch.Domain.Research;

namespace OrionSearch.Infrastructure.Persistence.Configurations;

public sealed class ArchiveMemberConfiguration : IEntityTypeConfiguration<ArchiveMember>
{
    public void Configure(EntityTypeBuilder<ArchiveMember> builder)
    {
        builder.ToTable("archive_members", "auth", table =>
        {
            table.HasCheckConstraint("ck_archive_members_role", "role IN ('owner', 'editor', 'viewer')");
        });
        builder.HasKey(x => new { x.ArchiveId, x.UserId }).HasName("pk_archive_members");
        builder.Property(x => x.ArchiveId).HasColumnName("archive_id").HasColumnType("uuid");
        builder.Property(x => x.UserId).HasColumnName("user_id").HasColumnType("uuid");
        builder.Property(x => x.Role).HasColumnName("role").HasColumnType("text").IsRequired().HasDefaultValue("viewer");
        builder.Property(x => x.JoinedAt).HasColumnName("joined_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("now()");

        builder.HasOne<Archive>().WithMany().HasForeignKey(x => x.ArchiveId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.UserId).HasDatabaseName("idx_archive_members_user");
        builder.HasIndex(x => x.ArchiveId).IsUnique().HasFilter("role = 'owner'").HasDatabaseName("ux_archive_members_owner");
    }
}
