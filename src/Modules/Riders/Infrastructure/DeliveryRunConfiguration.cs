using Courier.Branches.Domain;
using Courier.Riders.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Riders.Infrastructure;

public class DeliveryRunConfiguration : IEntityTypeConfiguration<DeliveryRun>
{
    public void Configure(EntityTypeBuilder<DeliveryRun> builder)
    {
        builder.ToTable("DeliveryRuns");
        builder.HasKey(r => r.Id);

        // The exact index RiderService.Tasks.cs's find-or-create query (Task 8) relies on: "does an
        // Active run already exist for (RiderId, BranchId, Date)".
        builder.HasIndex(r => new { r.RiderId, r.BranchId, r.Date });

        builder.HasOne<Rider>().WithMany().HasForeignKey(r => r.RiderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Branch>().WithMany().HasForeignKey(r => r.BranchId).OnDelete(DeleteBehavior.Restrict);

        // No OrganizationId of its own — tenant isolation flows transitively through
        // RiderId -> Rider.OrganizationId (Task 6's query filter on Rider already covers it).
    }
}
