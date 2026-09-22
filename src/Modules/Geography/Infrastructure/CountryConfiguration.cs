using Courier.Geography.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Geography.Infrastructure;

public class CountryConfiguration : IEntityTypeConfiguration<Country>
{
    // HasData rows need a fixed value for every column — DateTimeOffset.UtcNow here would make EF
    // regenerate this migration's snapshot diff on every build.
    private static readonly DateTimeOffset SeedTimestamp = new(2026, 9, 22, 0, 0, 0, TimeSpan.Zero);

    public void Configure(EntityTypeBuilder<Country> builder)
    {
        builder.ToTable("Countries");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.IsoCode).HasMaxLength(2).IsRequired();
        builder.HasIndex(c => c.IsoCode).IsUnique();
        builder.Property(c => c.NameEn).HasMaxLength(100).IsRequired();
        builder.Property(c => c.NameNe).HasMaxLength(100);
        builder.Property(c => c.DefaultCurrency).HasMaxLength(3).IsRequired();
        builder.Property(c => c.CallingCode).HasMaxLength(8).IsRequired();
        builder.HasOne<CountryZone>().WithMany().HasForeignKey(c => c.CountryZoneId).OnDelete(DeleteBehavior.SetNull);

        builder.HasData(CountrySeedData.Countries.Select(c => new Country
        {
            Id = GeographyIds.CountryId(c.IsoCode),
            IsoCode = c.IsoCode,
            NameEn = c.NameEn,
            NameNe = CountrySeedData.NameNe.GetValueOrDefault(c.IsoCode),
            DefaultCurrency = c.DefaultCurrency,
            CallingCode = c.CallingCode,
            IsServiced = false,
            CountryZoneId = null,
            CreatedAt = SeedTimestamp,
            UpdatedAt = SeedTimestamp,
        }));
    }
}
