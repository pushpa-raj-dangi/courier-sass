using Courier.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Identity.Infrastructure;

public class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("Organizations");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Name).HasMaxLength(200).IsRequired();
        builder.Property(o => o.Slug).HasMaxLength(100).IsRequired();
        builder.HasIndex(o => o.Slug).IsUnique();
        // Registration always allocates a readable, slug-derived prefix (AuthService). The database
        // default is a safety net so no Organizations row — however it is inserted (test seeds,
        // manual SQL) — is ever left without a unique prefix: 'Q' + 5 hex digits from a sequence
        // created in the AddOrganizationAwbPrefix migration. The empty-string sentinel makes EF
        // omit the column (and use this default) whenever the property was never set.
        builder.Property(o => o.AwbPrefix).HasMaxLength(AwbPrefix.MaxLength).IsRequired()
            .HasDefaultValueSql("'Q' || upper(lpad(to_hex(nextval('\"OrganizationAwbPrefixFallback\"')), 5, '0'))")
            .HasSentinel(string.Empty);
        builder.HasIndex(o => o.AwbPrefix).IsUnique();
        builder.Property(o => o.CreatedAt).HasDefaultValueSql("now()");
    }
}
