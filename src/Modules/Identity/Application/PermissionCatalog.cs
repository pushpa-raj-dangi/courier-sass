using System.Security.Cryptography;
using System.Text;

namespace Courier.Identity.Application;

public static class PermissionCatalog
{
    public static readonly IReadOnlyList<(string Key, string Description)> All = new (string, string)[]
    {
        ("shipment.create", "Create shipments"),
        ("shipment.read", "Read shipments"),
        ("shipment.update", "Update shipments"),
        ("shipment.cancel", "Cancel shipments"),
        ("shipment.assign", "Assign shipments to riders"),
        ("shipment.scan", "Scan shipments"),
        ("shipment.sort", "Sort shipments"),
        ("shipment.transfer", "Transfer shipments between branches"),
        ("delivery.assign", "Assign delivery tasks"),
        ("delivery.attempt", "Record delivery attempts"),
        ("delivery.complete", "Complete deliveries"),
        ("ndr.read", "Read NDRs"),
        ("ndr.manage", "Manage NDRs"),
        ("ndr.resolve", "Resolve NDRs"),
        ("rto.read", "Read RTOs"),
        ("rto.manage", "Manage RTOs"),
        ("cod.read", "Read COD records"),
        ("cod.collect", "Collect COD"),
        ("cod.reconcile", "Reconcile COD"),
        ("cod.settle", "Settle COD"),
        ("cod.adjust", "Adjust COD figures"),
        ("manifest.read", "Read manifests"),
        ("manifest.create", "Create manifests"),
        ("manifest.seal", "Seal manifests"),
        ("manifest.dispatch", "Dispatch manifests"),
        ("manifest.receive", "Receive manifests"),
        ("manifest.close", "Close manifests"),
        ("pickup.read", "Read merchant pickup requests"),
        ("branch.read", "Read branches"),
        ("branch.create", "Create branches"),
        ("branch.update", "Update branches"),
        ("geography.read", "Read geography reference data"),
        ("zone.read", "Read delivery zones"),
        ("zone.create", "Create delivery zones"),
        ("zone.update", "Update delivery zones"),
        ("merchant.read", "Read merchants"),
        ("merchant.create", "Create merchants"),
        ("merchant.update", "Update merchants"),
        ("merchant.kyc.review", "Review merchant KYC"),
        ("rider.read", "Read riders"),
        ("rider.create", "Create riders"),
        ("rider.assign", "Assign riders"),
        ("pricing.read", "Read pricing"),
        ("pricing.manage", "Manage pricing"),
        ("report.read", "Read reports"),
        ("report.export", "Export reports"),
        ("user.read", "Read users"),
        ("user.create", "Create users"),
        ("user.update", "Update users"),
        ("role.manage", "Manage roles"),
        ("audit.read", "Read audit logs"),
        ("org.billing.manage", "Manage organization billing"),
        ("org.ownership.transfer", "Transfer organization ownership"),
        ("org.delete", "Delete an organization"),
        ("org.settings.manage", "Manage organization profile, security policy and operational defaults"),
        ("notification_template.manage", "Manage notification templates"),
    };

    public static Guid IdFor(string key)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(key));
        return new Guid(hash);
    }
}
