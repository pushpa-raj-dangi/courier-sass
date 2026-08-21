using Courier.Ndr.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Ndr.Infrastructure;

public class NdrConfiguration : IEntityTypeConfiguration<NDR>
{
    public void Configure(EntityTypeBuilder<NDR> builder)
    {
        builder.ToTable("Ndrs");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.CustomerResponse).HasMaxLength(1000);
        builder.Property(n => n.NextAction).HasMaxLength(500);

        builder.HasIndex(n => n.OrganizationId);
        builder.HasIndex(n => n.ShipmentId);
        // Not unique: a shipment could in principle accumulate more than one NDR over its lifetime
        // (a second failed attempt after some future phase's successful reattempt would create a
        // second one) — only "no second Open NDR for the same shipment while one is already
        // unresolved" needs to hold, enforced in NdrCreationService.CreateFromFailedAttemptAsync
        // (Task 5), not a DB constraint.

        // No FK to Shipment: Ndr has a project reference to Shipments only for
        // IShipmentTransitionService (Application layer), not its EF model — same cross-module
        // precedent as ManifestShipment.ShipmentId (Phase 7) and DeliveryTask.ShipmentId (Phase 8).
    }
}
