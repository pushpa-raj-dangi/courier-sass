using Courier.Cod.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Cod.Infrastructure;

public class MerchantSettlementConfiguration : IEntityTypeConfiguration<MerchantSettlement>
{
    public void Configure(EntityTypeBuilder<MerchantSettlement> builder)
    {
        builder.ToTable("MerchantSettlements");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.TotalPayable).HasPrecision(18, 2);

        builder.HasIndex(m => m.OrganizationId);
        builder.HasIndex(m => m.MerchantId);
        // No FK to Merchant — Cod's project reference to Merchants is Application-layer only
        // (IMerchantLookupService), not its EF model, same cross-module precedent as
        // Shipment.MerchantId (Phase 6).
    }
}
