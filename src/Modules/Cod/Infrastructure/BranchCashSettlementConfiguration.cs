using Courier.Branches.Domain;
using Courier.Cod.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Cod.Infrastructure;

public class BranchCashSettlementConfiguration : IEntityTypeConfiguration<BranchCashSettlement>
{
    public void Configure(EntityTypeBuilder<BranchCashSettlement> builder)
    {
        builder.ToTable("BranchCashSettlements");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.ConfirmedAmount).HasPrecision(18, 2);

        builder.HasIndex(b => b.OrganizationId);
        builder.HasIndex(b => b.BranchId);

        builder.HasOne<Branch>().WithMany().HasForeignKey(b => b.BranchId).OnDelete(DeleteBehavior.Restrict);
    }
}
