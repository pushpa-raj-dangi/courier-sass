namespace Courier.Ndr.Domain;

// Created by Ndr.Application.NdrService.ResolveAsync when Resolution is Rto or AutoRto (Task 7).
// Deliberately does NOT store its own Status/CurrentBranchId/ReceivedAt columns, unlike
// DOMAIN-MODEL.md's original field sketch — the underlying Shipment (via OriginalShipmentId) is
// already the single source of truth for both (CurrentStatus walks RtoInitiated -> RtoInTransit ->
// RtoAtOrigin -> RtoDelivered, CurrentBranchId tracks the same physical custody Manifests already
// updates during the return leg). A second, independently-updated copy of either would need
// Manifests to gain a project reference to Ndr purely to keep it in sync — this plan avoids that
// cross-module coupling entirely by having ReturnShipmentService (Task 11) read the live values
// from IShipmentTransitionService.GetScanInfoAsync(OriginalShipmentId) instead, matching how
// Manifests.Application.ManifestService.GetMissingShipmentsAsync already enriches its own response
// with live shipment data the same way (CLAUDE.md §12: a status change alone is never treated as
// proof of physical transfer, and neither is a cached copy of one). MerchantConfirmedAt is the one
// exception — it's a genuine Ndr-module-owned fact (when staff confirmed the merchant accepted the
// return), set directly by ReturnShipmentService.ConfirmAsync with no cross-module coordination
// needed.
public class ReturnShipment
{
    public Guid Id { get; set; }
    public Guid OriginalShipmentId { get; set; }
    public Guid OrganizationId { get; set; }
    public required string ReturnAwb { get; set; }
    public required string RtoReason { get; set; }
    public Guid OriginBranchId { get; set; }
    public Guid DestinationBranchId { get; set; }
    public DateTimeOffset? MerchantConfirmedAt { get; set; }
}
