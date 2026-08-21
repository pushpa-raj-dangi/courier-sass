using Courier.Branches.Domain;
using Courier.Manifests.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Manifests.Infrastructure;

public class ManifestConfiguration : IEntityTypeConfiguration<Manifest>
{
    public void Configure(EntityTypeBuilder<Manifest> builder)
    {
        builder.ToTable("Manifests");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Code).HasMaxLength(30).IsRequired();
        builder.Property(m => m.VehiclePlate).HasMaxLength(30);

        // Every tenant-filtered query hits this column.
        builder.HasIndex(m => m.OrganizationId);
        // Code is unique per org — also the index GenerateManifestCodeAsync's collision-retry
        // relies on catching a violation of, mirroring Shipment.Awb's uniqueness (Phase 6).
        builder.HasIndex(m => new { m.OrganizationId, m.Code }).IsUnique();

        builder.HasOne<Branch>().WithMany().HasForeignKey(m => m.FromBranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Branch>().WithMany().HasForeignKey(m => m.ToBranchId).OnDelete(DeleteBehavior.Restrict);

        // No FK to Organization/User here: Manifests has no project reference to Identity —
        // wired centrally in Persistence/AppDbContext.OnModelCreating instead, same as Shipment.
    }
}
