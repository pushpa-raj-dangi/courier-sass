using Courier.Merchants.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Merchants.Infrastructure;

public class MerchantUserConfiguration : IEntityTypeConfiguration<MerchantUser>
{
    public void Configure(EntityTypeBuilder<MerchantUser> builder)
    {
        builder.ToTable("MerchantUsers");
        builder.HasKey(mu => mu.Id);
        builder.Property(mu => mu.Email).HasMaxLength(200).IsRequired();
        builder.Property(mu => mu.PasswordHash).IsRequired();
        builder.Property(mu => mu.FullName).HasMaxLength(200).IsRequired();

        // Globally unique across the platform (not just per-merchant), mirroring User.Email —
        // login resolves a MerchantUser by email alone, same pattern as staff login.
        builder.HasIndex(mu => mu.Email).IsUnique();

        builder.HasIndex(mu => mu.MerchantId);
        builder.HasIndex(mu => mu.OrganizationId);

        builder.HasOne<Merchant>()
            .WithMany()
            .HasForeignKey(mu => mu.MerchantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
