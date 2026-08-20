using Courier.Pricing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Pricing.Infrastructure;

public class AdditionalChargeConfiguration : IEntityTypeConfiguration<AdditionalCharge>
{
    public void Configure(EntityTypeBuilder<AdditionalCharge> builder)
    {
        builder.ToTable("AdditionalCharges");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Value).HasPrecision(18, 2);

        builder.HasIndex(a => a.RateCardId);

        builder.HasOne<RateCard>().WithMany().HasForeignKey(a => a.RateCardId).OnDelete(DeleteBehavior.Restrict);
    }
}
