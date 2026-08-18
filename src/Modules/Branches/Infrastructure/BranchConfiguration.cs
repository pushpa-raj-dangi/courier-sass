using Courier.Branches.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Branches.Infrastructure;

public class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("Branches");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Code).HasMaxLength(20).IsRequired();
        builder.Property(b => b.Name).HasMaxLength(200).IsRequired();
        builder.Property(b => b.AddressLine).HasMaxLength(500).IsRequired();
        builder.Property(b => b.Phone).HasMaxLength(30);
        builder.Property(b => b.OperatingHours).HasMaxLength(100);

        // Branch codes (e.g. "KTM-HUB") are unique per organization, not globally.
        builder.HasIndex(b => new { b.OrganizationId, b.Code }).IsUnique();

        // Every tenant-filtered query hits this column.
        builder.HasIndex(b => b.OrganizationId);
        builder.HasIndex(b => b.ParentBranchId);

        // Self-referencing hierarchy FK. Restrict: a branch with children cannot be deleted
        // out from under them by a cascading delete.
        builder.HasOne<Branch>()
            .WithMany()
            .HasForeignKey(b => b.ParentBranchId)
            .OnDelete(DeleteBehavior.Restrict);

        // No FK to Organization here: Branches has no project reference to Identity (see this
        // plan's Architecture section). That FK is configured centrally in
        // Persistence/AppDbContext.OnModelCreating instead.
    }
}
