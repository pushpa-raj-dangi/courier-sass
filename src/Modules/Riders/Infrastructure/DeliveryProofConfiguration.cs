using Courier.Riders.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Riders.Infrastructure;

public class DeliveryProofConfiguration : IEntityTypeConfiguration<DeliveryProof>
{
    public void Configure(EntityTypeBuilder<DeliveryProof> builder)
    {
        builder.ToTable("DeliveryProofs");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.RecipientName).HasMaxLength(200).IsRequired();
        builder.Property(p => p.SignatureStorageKey).HasMaxLength(500);
        builder.Property(p => p.PhotoStorageKey).HasMaxLength(500);
        builder.Property(p => p.CorrectionReason).HasMaxLength(500);
        builder.Property(p => p.Latitude).HasPrecision(9, 6);
        builder.Property(p => p.Longitude).HasPrecision(9, 6);

        builder.HasIndex(p => p.OrganizationId);
        builder.HasIndex(p => p.ShipmentId);
        // One proof per attempt.
        builder.HasIndex(p => p.DeliveryAttemptId).IsUnique();

        builder.HasOne<Rider>().WithMany().HasForeignKey(p => p.RiderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DeliveryAttempt>().WithMany().HasForeignKey(p => p.DeliveryAttemptId).OnDelete(DeleteBehavior.Cascade);

        // No FK to Shipment — same cross-module reasoning as DeliveryTaskConfiguration above.
    }
}
