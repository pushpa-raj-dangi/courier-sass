namespace Courier.Identity.Application;

// The 11 CLAUDE.md §6 default roles not already seeded by RegisterOrganizationAsync
// (OrganizationOwner, DeliveryRider, PickupRider predate this phase). Permission sets are
// grounded in docs/RBAC.md §4's illustrative coverage table where it names a role explicitly
// (BranchManager, FinanceManager/FinanceStaff, Auditor), reasoned from that table's role
// descriptions and CLAUDE.md §6's own role names otherwise. SuperAdmin (platform-level, no
// OrganizationId) and Merchant/MerchantStaff (docs/RBAC.md §5's separate MerchantUser identity
// track) are deliberately excluded — see CLARIFICATIONS.md #18.
public static class AdditionalRoleSeeds
{
    public static readonly IReadOnlyDictionary<string, string[]> ByRoleName = new Dictionary<string, string[]>
    {
        // "Same as Owner minus billing/ownership/delete" — docs/RBAC.md §2.
        ["OrganizationAdmin"] = PermissionCatalog.All
            .Select(p => p.Key)
            .Except(new[] { "org.billing.manage", "org.ownership.transfer", "org.delete" })
            .ToArray(),
        ["OperationsManager"] = new[]
        {
            "shipment.create", "shipment.read", "shipment.update", "shipment.cancel", "shipment.assign",
            "shipment.scan", "shipment.sort", "shipment.transfer", "delivery.assign",
            "manifest.read", "manifest.create", "manifest.seal", "manifest.dispatch", "manifest.receive", "manifest.close",
            "rider.read", "rider.location.read", "rider.assign", "ndr.read", "ndr.manage", "ndr.resolve", "rto.read", "rto.manage",
            "branch.read", "zone.read", "report.read"
        },
        // docs/RBAC.md §4's exact BranchManager example, plus a deliberate RBAC fix (this design's
        // spec §4.2): BranchManager never held rider.read at all before this — it's the realistic
        // day-to-day user of the new Live Map, so it gains rider.read and rider.location.read
        // together rather than being left out.
        ["BranchManager"] = new[]
        {
            "shipment.read", "shipment.assign", "shipment.scan",
            "manifest.read", "manifest.create", "manifest.seal", "manifest.dispatch", "manifest.receive", "manifest.close",
            "delivery.assign", "ndr.manage", "ndr.read", "cod.reconcile", "cod.read", "branch.read",
            "rider.read", "rider.location.read"
        },
        // Same rider.read/rider.location.read RBAC fix as BranchManager above — Dispatcher runs
        // day-to-day dispatch and had no visibility into riders at all before this.
        ["Dispatcher"] = new[]
        {
            "manifest.read", "manifest.create", "manifest.seal", "manifest.dispatch", "manifest.receive", "manifest.close",
            "shipment.transfer", "shipment.read", "branch.read",
            "rider.read", "rider.location.read"
        },
        ["SortingStaff"] = new[] { "shipment.scan", "shipment.sort", "manifest.read" },
        ["CounterStaff"] = new[] { "shipment.create", "shipment.read", "shipment.update", "pricing.read" },
        // docs/RBAC.md §4: "cod.* including cod.adjust and cod.settle, report.read/export".
        ["FinanceManager"] = new[] { "cod.read", "cod.collect", "cod.reconcile", "cod.settle", "cod.adjust", "report.read", "report.export" },
        // docs/RBAC.md §4: "COD reconciliation, no adjustment approval" — settle also excluded,
        // an approval-adjacent action, matching FinanceManager/FinanceStaff's stated split.
        ["FinanceStaff"] = new[] { "cod.read", "cod.reconcile" },
        ["CustomerSupport"] = new[] { "shipment.read", "ndr.read", "ndr.resolve" },
        // docs/RBAC.md §4: "*.read + audit.read across the org, no write permissions anywhere."
        ["Auditor"] = PermissionCatalog.All
            .Select(p => p.Key)
            .Where(k => k.EndsWith(".read") || k == "audit.read")
            .ToArray(),
        ["HubStaff"] = new[]
        {
            "manifest.read", "manifest.create", "manifest.seal", "manifest.dispatch", "manifest.receive", "manifest.close",
            "shipment.scan", "shipment.sort", "branch.read"
        },
    };
}
