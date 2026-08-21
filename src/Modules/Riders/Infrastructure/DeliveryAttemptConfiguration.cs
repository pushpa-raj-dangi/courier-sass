using Courier.Riders.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Riders.Infrastructure;

public class DeliveryAttemptConfiguration : IEntityTypeConfiguration<DeliveryAttempt>
{
    public void Configure(EntityTypeBuilder<DeliveryAttempt> builder)
    {
        builder.ToTable("DeliveryAttempts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Notes).HasMaxLength(1000);
        builder.Property(a => a.NextAction).HasMaxLength(500);
        builder.Property(a => a.Latitude).HasPrecision(9, 6);
        builder.Property(a => a.Longitude).HasPrecision(9, 6);

        builder.HasIndex(a => a.OrganizationId);
        builder.HasIndex(a => a.ShipmentId);
        builder.HasIndex(a => a.RiderId);

        builder.HasOne<Rider>().WithMany().HasForeignKey(a => a.RiderId).OnDelete(DeleteBehavior.Restrict);

        // No FK to Shipment — same cross-module reasoning as DeliveryTaskConfiguration above.
    }
}
