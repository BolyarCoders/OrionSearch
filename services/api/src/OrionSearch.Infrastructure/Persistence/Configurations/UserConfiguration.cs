using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrionSearch.Domain.Auth;

namespace OrionSearch.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", "auth", table =>
        {
            table.HasCheckConstraint("ck_users_status", "status IN ('active', 'disabled')");
            table.HasCheckConstraint("ck_users_email", "btrim(email::text) <> ''");
            table.HasCheckConstraint("ck_users_display_name", "btrim(display_name) <> ''");
            table.HasCheckConstraint("ck_users_access_failed_count", "access_failed_count >= 0");
        });
        builder.HasKey(x => x.Id).HasName("pk_users");
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.Email).HasColumnName("email").HasColumnType("citext").IsRequired();
        builder.Property(x => x.PasswordHash).HasColumnName("password_hash").HasColumnType("text");
        builder.Property(x => x.NormalizedEmail).HasColumnName("normalized_email").HasColumnType("text");
        builder.Property(x => x.EmailConfirmed).HasColumnName("email_confirmed").HasDefaultValue(false);
        builder.Property(x => x.SecurityStamp).HasColumnName("security_stamp").HasColumnType("text").IsRequired();
        builder.Property(x => x.ConcurrencyStamp).HasColumnName("concurrency_stamp").HasColumnType("text").IsConcurrencyToken().IsRequired();
        builder.Property(x => x.AccessFailedCount).HasColumnName("access_failed_count").HasDefaultValue(0);
        builder.Property(x => x.LockoutEnd).HasColumnName("lockout_end").HasColumnType("timestamp with time zone");
        builder.Property(x => x.LockoutEnabled).HasColumnName("lockout_enabled").HasDefaultValue(true);
        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasColumnType("text").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasColumnType("text").IsRequired().HasDefaultValue("active");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();

        builder.HasIndex(x => x.Email).IsUnique().HasDatabaseName("ux_users_email");
        builder.HasIndex(x => x.NormalizedEmail).IsUnique().HasDatabaseName("ux_users_normalized_email");
    }
}
