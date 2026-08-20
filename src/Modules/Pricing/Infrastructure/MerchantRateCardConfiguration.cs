using Courier.Merchants.Domain;
using Courier.Pricing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Pricing.Infrastructure;

public class MerchantRateCardConfiguration : IEntityTypeConfiguration<MerchantRateCard>
{
    public void Configure(EntityTypeBuilder<MerchantRateCard> builder)
    {
        builder.ToTable("MerchantRateCards");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.OverrideDiscountPercent).HasPrecision(5, 2);

        builder.HasIndex(m => m.OrganizationId);
        // At most one MerchantRateCard row per merchant (RateCardService.UpsertMerchantRateCardAsync
        // relies on this to decide insert vs. update).
        builder.HasIndex(m => m.MerchantId).IsUnique();

        builder.HasOne<RateCard>().WithMany().HasForeignKey(m => m.RateCardId).OnDelete(DeleteBehavior.Restrict);

        // Merchants FK: Pricing has a project reference to Merchants (Task 1), so configured
        // directly here, mirroring Branches'/Merchants' Geography FK pattern.
        builder.HasOne<Merchant>().WithMany().HasForeignKey(m => m.MerchantId).OnDelete(DeleteBehavior.Restrict);

        // No FK to Organization here: Pricing has no project reference to Identity — wired
        // centrally in Persistence/AppDbContext.OnModelCreating instead (Task 9), same reasoning
        // as DeliveryZone/Merchant.
    }
}
