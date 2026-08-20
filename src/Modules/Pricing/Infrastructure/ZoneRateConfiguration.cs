using Courier.Geography.Domain;
using Courier.Pricing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Pricing.Infrastructure;

public class ZoneRateConfiguration : IEntityTypeConfiguration<ZoneRate>
{
    public void Configure(EntityTypeBuilder<ZoneRate> builder)
    {
        builder.ToTable("ZoneRates");
        builder.HasKey(z => z.Id);
        builder.Property(z => z.BaseRate).HasPrecision(18, 2);

        builder.HasIndex(z => z.RateCardId);
        // A RateCard should have at most one BaseRate for a given route/service/weight
        // combination — this is the exact tuple PricingService.QuoteAsync looks up by.
        builder.HasIndex(z => new { z.RateCardId, z.OriginZoneId, z.DestinationZoneId, z.ServiceTypeId, z.WeightSlabId }).IsUnique();

        builder.HasOne<RateCard>().WithMany().HasForeignKey(z => z.RateCardId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ServiceType>().WithMany().HasForeignKey(z => z.ServiceTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WeightSlab>().WithMany().HasForeignKey(z => z.WeightSlabId).OnDelete(DeleteBehavior.Restrict);

        // Geography FKs: Pricing has a project reference to Geography (Task 1), so these are
        // configured directly here, mirroring Branches'/Merchants' Province/District FK pattern.
        // Two independent FKs to the same DeliveryZone table (origin and destination) — fine,
        // there's no navigation collection on DeliveryZone for either side to bind to.
        builder.HasOne<DeliveryZone>().WithMany().HasForeignKey(z => z.OriginZoneId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DeliveryZone>().WithMany().HasForeignKey(z => z.DestinationZoneId).OnDelete(DeleteBehavior.Restrict);
    }
}
