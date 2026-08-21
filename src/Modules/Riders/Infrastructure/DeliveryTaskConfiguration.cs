using Courier.Riders.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Riders.Infrastructure;

public class DeliveryTaskConfiguration : IEntityTypeConfiguration<DeliveryTask>
{
    public void Configure(EntityTypeBuilder<DeliveryTask> builder)
    {
        builder.ToTable("DeliveryTasks");
        builder.HasKey(t => t.Id);

        builder.HasIndex(t => t.DeliveryRunId);
        // Not unique: a shipment can get a second DeliveryTask on a later run once Phase 9 adds a
        // reattempt loop — only "one row per (DeliveryRunId, ShipmentId, Type)" needs to hold in
        // practice, which the natural assignment path already guarantees this phase.
        builder.HasIndex(t => t.ShipmentId);

        builder.HasOne<DeliveryRun>().WithMany().HasForeignKey(t => t.DeliveryRunId).OnDelete(DeleteBehavior.Cascade);

        // No FK to Shipment: even though Riders has a project reference to Shipments (for
        // IShipmentTransitionService), the same precedent as ManifestShipment.ShipmentId (Phase 7)
        // and Shipment.CurrentCustodianUserId (Phase 6) applies — cross-module id fields are left
        // unconstrained at the DB level in this codebase rather than every referencing module
        // adding a real FK into another module's table. No own OrganizationId — flows transitively
        // through DeliveryRunId -> DeliveryRun -> RiderId -> Rider.OrganizationId.
    }
}
