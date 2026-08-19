using Courier.Geography.Domain;
using Courier.Merchants.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Merchants.Infrastructure;

public class MerchantAddressConfiguration : IEntityTypeConfiguration<MerchantAddress>
{
    public void Configure(EntityTypeBuilder<MerchantAddress> builder)
    {
        builder.ToTable("MerchantAddresses");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Label).HasMaxLength(100).IsRequired();
        builder.Property(a => a.AddressLine).HasMaxLength(500).IsRequired();

        builder.HasIndex(a => a.OrganizationId);
        builder.HasIndex(a => a.MerchantId);
        builder.HasIndex(a => a.ProvinceId);
        builder.HasIndex(a => a.DistrictId);
        builder.HasIndex(a => a.LocalLevelId);

        builder.HasOne<Merchant>()
            .WithMany()
            .HasForeignKey(a => a.MerchantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Geography FKs: Merchants has a project reference to Geography (Task 1), so these are
        // configured directly here, mirroring Branches' BranchConfiguration.
        builder.HasOne<Province>()
            .WithMany()
            .HasForeignKey(a => a.ProvinceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<District>()
            .WithMany()
            .HasForeignKey(a => a.DistrictId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LocalLevel>()
            .WithMany()
            .HasForeignKey(a => a.LocalLevelId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
