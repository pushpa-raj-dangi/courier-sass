using Courier.Pricing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Pricing.Infrastructure;

public class WeightSlabConfiguration : IEntityTypeConfiguration<WeightSlab>
{
    public void Configure(EntityTypeBuilder<WeightSlab> builder)
    {
        builder.ToTable("WeightSlabs");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.MinWeightKg).HasPrecision(10, 3);
        builder.Property(w => w.MaxWeightKg).HasPrecision(10, 3);

        builder.HasData(PricingSeedData.WeightSlabs.Select(w => new WeightSlab
        {
            Id = PricingIds.WeightSlabId(w.MinWeightKg, w.MaxWeightKg),
            MinWeightKg = w.MinWeightKg,
            MaxWeightKg = w.MaxWeightKg
        }));

        // No tenant filter: shared reference data (see ServiceTypeConfiguration).
    }
}
