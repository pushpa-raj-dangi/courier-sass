using Courier.Branches.Domain;
using Courier.Ndr.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Ndr.Infrastructure;

public class ReturnShipmentConfiguration : IEntityTypeConfiguration<ReturnShipment>
{
    public void Configure(EntityTypeBuilder<ReturnShipment> builder)
    {
        builder.ToTable("ReturnShipments");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.ReturnAwb).HasMaxLength(30).IsRequired();
        builder.Property(r => r.RtoReason).HasMaxLength(500).IsRequired();

        builder.HasIndex(r => r.OrganizationId);
        // A shipment can only ever reach RTO_INITIATED once (there is no transition back from
        // RtoInitiated/RtoInTransit/RtoAtOrigin/RtoDelivered to NdrCreated), so at most one
        // ReturnShipment per original shipment ever exists.
        builder.HasIndex(r => r.OriginalShipmentId).IsUnique();
        // ReturnAwb is unique per org, same shape as Shipment.Awb/Manifest.Code — also the index
        // GenerateReturnAwbAsync's collision-retry relies on catching a violation of.
        builder.HasIndex(r => new { r.OrganizationId, r.ReturnAwb }).IsUnique();

        builder.HasOne<Branch>().WithMany().HasForeignKey(r => r.OriginBranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Branch>().WithMany().HasForeignKey(r => r.DestinationBranchId).OnDelete(DeleteBehavior.Restrict);

        // No FK to Shipment (OriginalShipmentId) — same cross-module reasoning as NdrConfiguration
        // above. No FK to Organization here: wired centrally in
        // Persistence/AppDbContext.OnModelCreating (Task 4), same as every other module.
    }
}
