using Courier.Branches.Domain;
using Courier.Manifests.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Manifests.Infrastructure;

public class ShipmentTransferConfiguration : IEntityTypeConfiguration<ShipmentTransfer>
{
    public void Configure(EntityTypeBuilder<ShipmentTransfer> builder)
    {
        builder.ToTable("ShipmentTransfers");
        builder.HasKey(t => t.Id);

        builder.HasIndex(t => t.ShipmentId);
        builder.HasOne<Manifest>().WithMany().HasForeignKey(t => t.ManifestId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Branch>().WithMany().HasForeignKey(t => t.FromBranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Branch>().WithMany().HasForeignKey(t => t.ToBranchId).OnDelete(DeleteBehavior.Restrict);

        // No OrganizationId of its own: same reasoning as ManifestShipmentConfiguration.
    }
}
