using Courier.International.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.International.Infrastructure;

public class InternationalLegConfiguration : IEntityTypeConfiguration<InternationalLeg>
{
    public void Configure(EntityTypeBuilder<InternationalLeg> builder)
    {
        builder.ToTable("InternationalLegs");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(l => l.ContentsDescription).HasMaxLength(1000).IsRequired();
        builder.Property(l => l.CountryOfOrigin).HasMaxLength(2).IsRequired();
        builder.Property(l => l.ExportLicenceReference).HasMaxLength(200);
        builder.Property(l => l.ProvisionalTotalChargeAmount).HasPrecision(18, 2);
        builder.Property(l => l.ProvisionalTotalChargeCurrency).HasMaxLength(3).IsRequired();

        builder.HasIndex(l => l.OrganizationId);
        // One-to-one by ShipmentId — I3 design spec §3.
        builder.HasIndex(l => l.ShipmentId).IsUnique();
    }
}
