using Courier.Branches.Domain;
using Courier.Riders.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Riders.Infrastructure;

public class RiderConfiguration : IEntityTypeConfiguration<Rider>
{
    public void Configure(EntityTypeBuilder<Rider> builder)
    {
        builder.ToTable("Riders");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.LicenseNumber).HasMaxLength(50).IsRequired();

        builder.HasIndex(r => r.OrganizationId);
        // One Rider per User — a User can't simultaneously be two different riders.
        builder.HasIndex(r => r.UserId).IsUnique();

        builder.HasOne<Branch>().WithMany().HasForeignKey(r => r.BaseBranchId).OnDelete(DeleteBehavior.Restrict);

        // No FK to Organization/User here: Organization FK wiring is centralized in
        // Persistence/AppDbContext.OnModelCreating (Task 6), same as every other module. UserId is
        // left unconstrained at the DB level for the same reason Shipment.CurrentCustodianUserId is
        // (Phase 6) — nothing in this phase queries through it as a join, only stores it.
    }
}
