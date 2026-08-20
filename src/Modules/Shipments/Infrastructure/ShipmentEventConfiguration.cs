using Courier.Branches.Domain;
using Courier.Shipments.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Shipments.Infrastructure;

public class ShipmentEventConfiguration : IEntityTypeConfiguration<ShipmentEvent>
{
    public void Configure(EntityTypeBuilder<ShipmentEvent> builder)
    {
        builder.ToTable("ShipmentEvents");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.Property(e => e.Metadata).HasColumnType("jsonb");
        builder.Property(e => e.Latitude).HasPrecision(9, 6);
        builder.Property(e => e.Longitude).HasPrecision(9, 6);

        // Query pattern: "give me this shipment's full event history in order" — this index
        // makes that cheap; Timestamp is included so the common ORDER BY needs no extra sort.
        builder.HasIndex(e => new { e.ShipmentId, e.Timestamp });
        builder.HasOne<Shipment>().WithMany().HasForeignKey(e => e.ShipmentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Branch>().WithMany().HasForeignKey(e => e.BranchId).OnDelete(DeleteBehavior.Restrict);

        // No OrganizationId of its own: same reasoning as ShipmentItemConfiguration.
    }
}
