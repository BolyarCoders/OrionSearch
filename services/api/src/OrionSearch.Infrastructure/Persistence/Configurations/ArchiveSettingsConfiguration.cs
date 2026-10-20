using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrionSearch.Domain.Research;

namespace OrionSearch.Infrastructure.Persistence.Configurations;

public sealed class ArchiveSettingsConfiguration : IEntityTypeConfiguration<ArchiveSettings>
{
    public void Configure(EntityTypeBuilder<ArchiveSettings> builder)
    {
        builder.ToTable("archive_settings", "research", table =>
        {
            table.HasCheckConstraint("ck_archive_settings_dates", "date_from IS NULL OR date_to IS NULL OR date_from <= date_to");
            table.HasCheckConstraint("ck_archive_settings_config", "jsonb_typeof(config) = 'object'");
        });
        builder.HasKey(x => x.ArchiveId).HasName("pk_archive_settings");
        builder.Property(x => x.ArchiveId).HasColumnName("archive_id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(x => x.DateFrom).HasColumnName("date_from").HasColumnType("date");
        builder.Property(x => x.DateTo).HasColumnName("date_to").HasColumnType("date");
        builder.Property(x => x.Languages).HasColumnName("languages").HasColumnType("text[]").IsRequired().HasDefaultValueSql("'{}'::text[]");
        builder.Property(x => x.PreferredDomains).HasColumnName("preferred_domains").HasColumnType("text[]").IsRequired().HasDefaultValueSql("'{}'::text[]");
        builder.Property(x => x.ExcludedDomains).HasColumnName("excluded_domains").HasColumnType("text[]").IsRequired().HasDefaultValueSql("'{}'::text[]");
        builder.Property(x => x.Config).HasColumnName("config").HasColumnType("jsonb").IsRequired().HasDefaultValueSql("'{}'::jsonb");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();

        builder.HasOne<Archive>().WithOne().HasForeignKey<ArchiveSettings>(x => x.ArchiveId).OnDelete(DeleteBehavior.Cascade);
    }
}
