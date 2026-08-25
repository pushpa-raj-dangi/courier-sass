using Courier.Cod.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Cod.Infrastructure;

public class MerchantSettlementItemConfiguration : IEntityTypeConfiguration<MerchantSettlementItem>
{
    public void Configure(EntityTypeBuilder<MerchantSettlementItem> builder)
    {
        builder.ToTable("MerchantSettlementItems");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.CODAmount).HasPrecision(18, 2);
        builder.Property(i => i.CourierFee).HasPrecision(18, 2);
        builder.Property(i => i.OtherDeductions).HasPrecision(18, 2);
        builder.Property(i => i.NetPayable).HasPrecision(18, 2);

        builder.HasIndex(i => i.ShipmentId).IsUnique();
        // A shipment can only ever be folded into one MerchantSettlement — enforced here, matching
        // CODCollection.MerchantSettlementItemId's stamped-once usage in CreateMerchantSettlementAsync
        // (Task 9).

        builder.HasOne<MerchantSettlement>().WithMany().HasForeignKey(i => i.MerchantSettlementId).OnDelete(DeleteBehavior.Cascade);
    }
}
