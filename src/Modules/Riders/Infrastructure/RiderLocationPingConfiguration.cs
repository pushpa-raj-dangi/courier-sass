using Courier.Riders.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Riders.Infrastructure;

public class RiderLocationPingConfiguration : IEntityTypeConfiguration<RiderLocationPing>
{
    public void Configure(EntityTypeBuilder<RiderLocationPing> builder)
    {
        builder.ToTable("RiderLocationPings");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Latitude).HasPrecision(9, 6);
        builder.Property(p => p.Longitude).HasPrecision(9, 6);
        builder.Property(p => p.AccuracyMeters).HasPrecision(9, 2);

        // Covers RiderService.Location.cs's per-rider "latest ping" lookup (Task 3).
        builder.HasIndex(p => new { p.RiderId, p.RecordedAt });
        // Covers the breadcrumb-by-run read (Task 3).
        builder.HasIndex(p => p.DeliveryRunId);
        // Covers the retention job's age-based delete, now filtered by the server-anchored
        // ReceivedAt rather than the client-reported RecordedAt (finding: client clock skew must
        // not affect what gets pruned).
        builder.HasIndex(p => p.ReceivedAt);

        builder.HasOne<Rider>().WithMany().HasForeignKey(p => p.RiderId).OnDelete(DeleteBehavior.Cascade);
        // SetNull, not Cascade: DeliveryRun rows are never deleted anywhere in this codebase today,
        // but a ping losing its run association (rather than being deleted with it) is the correct
        // semantic if that ever changes — the ping itself is still a real position fact.
        builder.HasOne<DeliveryRun>().WithMany().HasForeignKey(p => p.DeliveryRunId).OnDelete(DeleteBehavior.SetNull);

        // No OrganizationId of its own — tenant isolation flows transitively through
        // RiderId -> Rider.OrganizationId (Rider's own HasQueryFilter already covers it for every
        // lookup that resolves Rider first, per this plan's Global Constraints).
    }
}
