using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrionSearch.Domain.Content;

namespace OrionSearch.Infrastructure.Persistence.Configurations;

public sealed class PublisherConfiguration : IEntityTypeConfiguration<Publisher>
{
    public void Configure(EntityTypeBuilder<Publisher> builder)
    {
        builder.ToTable("publishers", "content", table =>
        {
            table.HasCheckConstraint("ck_publishers_domain", "domain = lower(domain) AND domain ~ '^[a-z0-9]([a-z0-9.-]*[a-z0-9])?$'");
            table.HasCheckConstraint("ck_publishers_name", "btrim(name) <> ''");
        });
        builder.HasKey(x => x.Id).HasName("pk_publishers");
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.Name).HasColumnName("name").HasColumnType("text").IsRequired();
        builder.Property(x => x.Domain).HasColumnName("domain").HasColumnType("text").IsRequired();
        builder.Property(x => x.HomepageUrl).HasColumnName("homepage_url").HasColumnType("text");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("now()");

        builder.Property(x => x.Domain).HasConversion(value => DomainName.Normalize(value), value => value);
        builder.HasIndex(x => x.Domain).IsUnique().HasDatabaseName("ux_publishers_domain");
    }
}
