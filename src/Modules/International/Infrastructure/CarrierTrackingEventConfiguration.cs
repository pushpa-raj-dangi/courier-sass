using Courier.International.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.International.Infrastructure;

public class CarrierTrackingEventConfiguration : IEntityTypeConfiguration<CarrierTrackingEvent>
{
    public void Configure(EntityTypeBuilder<CarrierTrackingEvent> builder)
    {
        builder.ToTable("CarrierTrackingEvents");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.CarrierAwb).HasMaxLength(100).IsRequired();
        builder.Property(e => e.CarrierEventId).HasMaxLength(200).IsRequired();
        builder.Property(e => e.CarrierStatusCode).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(1000);
        builder.Property(e => e.Location).HasMaxLength(300);
        builder.Property(e => e.RawPayload).HasColumnType("jsonb");
        builder.Property(e => e.ProcessingNote).HasMaxLength(1000);
        builder.Property(e => e.MappedShipmentStatus).HasMaxLength(50);
        builder.Property(e => e.ReceivedAt).HasDefaultValueSql("now()");

        builder.HasOne<CarrierAccount>().WithMany().HasForeignKey(e => e.CarrierAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CarrierBooking>().WithMany().HasForeignKey(e => e.CarrierBookingId).OnDelete(DeleteBehavior.Restrict);

        // Deduplication across every source (webhook, poll, manual).
        builder.HasIndex(e => new { e.CarrierAccountId, e.CarrierEventId }).IsUnique();
        builder.HasIndex(e => new { e.ProcessingStatus, e.OccurredAt });
        builder.HasIndex(e => e.CarrierBookingId);
        builder.HasIndex(e => new { e.CarrierAccountId, e.CarrierAwb });
    }
}
