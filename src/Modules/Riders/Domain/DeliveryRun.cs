namespace Courier.Riders.Domain;

// Auto-created/reused (this plan's resolved design decision, no explicit "start my run" endpoint):
// RiderService.Tasks.cs finds-or-creates today's Active run for (RiderId, BranchId) the first time
// a task is assigned to that rider for that branch that day. No own OrganizationId — tenant
// isolation flows transitively through RiderId -> Rider.OrganizationId, same pattern as
// ManifestShipment -> Manifest.OrganizationId (Phase 7).
public class DeliveryRun
{
    public Guid Id { get; set; }
    public Guid RiderId { get; set; }
    public Guid BranchId { get; set; }
    public DateOnly Date { get; set; }
    public DeliveryRunStatus Status { get; set; }
}
