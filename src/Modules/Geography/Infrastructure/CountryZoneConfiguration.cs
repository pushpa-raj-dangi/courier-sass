using Courier.Geography.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Geography.Infrastructure;

public class CountryZoneConfiguration : IEntityTypeConfiguration<CountryZone>
{
    public void Configure(EntityTypeBuilder<CountryZone> builder)
    {
        builder.ToTable("CountryZones");
        builder.HasKey(z => z.Id);
        builder.Property(z => z.Name).HasMaxLength(150).IsRequired();
        builder.HasIndex(z => z.Name).IsUnique();
        builder.Property(z => z.Description).HasMaxLength(500);
    }
}
