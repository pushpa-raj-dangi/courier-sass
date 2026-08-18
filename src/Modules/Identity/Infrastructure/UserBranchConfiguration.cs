using Courier.Branches.Domain;
using Courier.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Identity.Infrastructure;

public class UserBranchConfiguration : IEntityTypeConfiguration<UserBranch>
{
    public void Configure(EntityTypeBuilder<UserBranch> builder)
    {
        builder.ToTable("UserBranches");
        builder.HasKey(ub => ub.Id);

        // Every tenant-filtered query hits this column.
        builder.HasIndex(ub => ub.OrganizationId);
        // Not unique: a new grant row is added after an old one expires, preserving history.
        builder.HasIndex(ub => new { ub.UserId, ub.BranchId });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(ub => ub.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Branch>()
            .WithMany()
            .HasForeignKey(ub => ub.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(ub => ub.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
