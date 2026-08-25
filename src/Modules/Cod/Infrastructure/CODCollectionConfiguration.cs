using Courier.Cod.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Cod.Infrastructure;

public class CODCollectionConfiguration : IEntityTypeConfiguration<CODCollection>
{
    public void Configure(EntityTypeBuilder<CODCollection> builder)
    {
        builder.ToTable("CODCollections");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.ExpectedAmount).HasPrecision(18, 2);
        builder.Property(c => c.ShipmentTotalCharge).HasPrecision(18, 2);
        builder.Property(c => c.CollectedAmount).HasPrecision(18, 2);

        builder.HasIndex(c => c.OrganizationId);
        builder.HasIndex(c => c.MerchantId);
        builder.HasIndex(c => c.BranchId);
        builder.HasIndex(c => c.Status);
        // At most one CODCollection per shipment ever — enforced at the database, not just by
        // CreateExpectedAsync only being called once per shipment at booking (this plan's eager
        // creation decision).
        builder.HasIndex(c => c.ShipmentId).IsUnique();

        // No FK to Shipment (ShipmentId) or DeliveryProof (DeliveryProofId) — Cod has no project
        // reference to Shipments or Riders' EF model, same cross-module precedent as
        // ManifestShipment.ShipmentId (Phase 7) and NDR.ShipmentId (Phase 9).
        builder.HasOne<RiderCashSettlement>().WithMany().HasForeignKey(c => c.RiderCashSettlementId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MerchantSettlementItem>().WithMany().HasForeignKey(c => c.MerchantSettlementItemId).OnDelete(DeleteBehavior.Restrict);
    }
}
