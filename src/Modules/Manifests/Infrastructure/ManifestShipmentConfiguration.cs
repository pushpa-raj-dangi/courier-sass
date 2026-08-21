using Courier.Manifests.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Manifests.Infrastructure;

public class ManifestShipmentConfiguration : IEntityTypeConfiguration<ManifestShipment>
{
    public void Configure(EntityTypeBuilder<ManifestShipment> builder)
    {
        builder.ToTable("ManifestShipments");
        builder.HasKey(ms => ms.Id);

        builder.HasIndex(ms => ms.ManifestId);
        // Not unique on ShipmentId: a shipment legitimately gets a second ManifestShipment row on
        // a later return-leg manifest once Phase 9 adds RTO — only "one row per (ManifestId,
        // ShipmentId) pair" needs to hold, which the natural insert path already guarantees (Sort
        // moves the shipment out of AtOriginBranch, so a second /scan attempt against the SAME
        // manifest fails the shipment-state check in ManifestService.ScanAsync before reaching here).
        builder.HasIndex(ms => ms.ShipmentId);

        builder.HasOne<Manifest>().WithMany().HasForeignKey(ms => ms.ManifestId).OnDelete(DeleteBehavior.Cascade);

        // No OrganizationId of its own: tenant isolation flows transitively through
        // ManifestId -> Manifest.OrganizationId, same reasoning as ShipmentItem/ShipmentEvent in
        // Phase 6. Shipments has no project reference back to Manifests, so ShipmentId is left
        // unconstrained at the DB level here (mirrors Shipment.CurrentCustodianUserId having no
        // FK) — every query against this table in Manifests.Application scopes through a Manifest
        // the caller's tenant already owns.
    }
}
