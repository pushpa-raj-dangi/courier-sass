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
        // (a second failed attempt after a successful reattempt would create a second one) — only
        // "no second Open NDR for the same shipment while one is already unresolved" needs to hold.
        // NdrCreationService.CreateFromFailedAttemptAsync has no explicit check for this; the
        // invariant holds transitively via the Shipment state machine instead: a new NDR is only
        // ever created right after ShipmentService.FailDeliveryAsync moves the shipment
        // OutForDelivery -> DeliveryFailed, and the only way back to OutForDelivery from
        // NdrCreated is ReattemptDeliveryAsync, which only NdrService.ResolveAsync calls — and only
        // once it has confirmed the existing NDR is ReattemptScheduled and is about to mark that
        // same NDR Resolved in the same call. So a shipment cannot reach DeliveryFailed/NdrCreated a
        // second time without its prior NDR already being resolved first. Not a DB constraint.

        // No FK to Shipment: Ndr has a project reference to Shipments only for
        // IShipmentTransitionService (Application layer), not its EF model — same cross-module
        // precedent as ManifestShipment.ShipmentId (Phase 7) and DeliveryTask.ShipmentId (Phase 8).
    }
}
