using Courier.Branches.Domain;
using Courier.Cod.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Cod.Infrastructure;

public class RiderCashSettlementConfiguration : IEntityTypeConfiguration<RiderCashSettlement>
{
    public void Configure(EntityTypeBuilder<RiderCashSettlement> builder)
    {
        builder.ToTable("RiderCashSettlements");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.DeclaredAmount).HasPrecision(18, 2);

        builder.HasIndex(r => r.OrganizationId);
        builder.HasIndex(r => r.RiderId);
        builder.HasIndex(r => r.BranchCashSettlementId);

        builder.HasOne<Branch>().WithMany().HasForeignKey(r => r.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BranchCashSettlement>().WithMany().HasForeignKey(r => r.BranchCashSettlementId).OnDelete(DeleteBehavior.Restrict);
        // No FK to Rider (RiderId) — Cod has no project reference to Riders' EF model.
    }
}
