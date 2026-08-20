using Courier.Shipments.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Shipments.Infrastructure;

public class ShipmentItemConfiguration : IEntityTypeConfiguration<ShipmentItem>
{
    public void Configure(EntityTypeBuilder<ShipmentItem> builder)
    {
        builder.ToTable("ShipmentItems");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Description).HasMaxLength(500).IsRequired();
        builder.Property(i => i.UnitValue).HasPrecision(18, 2);

        builder.HasIndex(i => i.ShipmentId);
        builder.HasOne<Shipment>().WithMany().HasForeignKey(i => i.ShipmentId).OnDelete(DeleteBehavior.Cascade);

        // No OrganizationId of its own: tenant isolation flows transitively through
        // ShipmentId -> Shipment.OrganizationId, same reasoning as ZoneRate/AdditionalCharge
        // in Phase 5. Every query against this table scopes through a Shipment the caller's
        // tenant already owns.
    }
}
