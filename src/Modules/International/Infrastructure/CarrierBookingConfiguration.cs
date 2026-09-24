using Courier.International.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.International.Infrastructure;

public class CarrierBookingConfiguration : IEntityTypeConfiguration<CarrierBooking>
{
    public void Configure(EntityTypeBuilder<CarrierBooking> builder)
    {
        builder.ToTable("CarrierBookings");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.CarrierAwb).HasMaxLength(100);
        builder.Property(b => b.ServiceLevel).HasMaxLength(100);
        builder.Property(b => b.LabelStorageKey).HasMaxLength(500);
        builder.Property(b => b.FailureReason).HasMaxLength(1000);
        builder.Property(b => b.RawResponse).HasColumnType("jsonb");
        builder.Property(b => b.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder.Property(b => b.CreatedAt).HasDefaultValueSql("now()");

        builder.HasOne<InternationalLeg>().WithMany().HasForeignKey(b => b.InternationalLegId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CarrierAccount>().WithMany().HasForeignKey(b => b.CarrierAccountId).OnDelete(DeleteBehavior.Restrict);

        // At most one live booking (Pending = 0, Booked = 1) per leg; Failed/Cancelled are history.
        builder.HasIndex(b => b.InternationalLegId).IsUnique().HasFilter("\"Status\" IN (0, 1)");
        // Ingest resolves an event to exactly one Booked booking by (account, carrier AWB).
        builder.HasIndex(b => new { b.CarrierAccountId, b.CarrierAwb }).IsUnique().HasFilter("\"CarrierAwb\" IS NOT NULL AND \"Status\" = 1");
        builder.HasIndex(b => new { b.OrganizationId, b.IdempotencyKey }).IsUnique();
    }
}
