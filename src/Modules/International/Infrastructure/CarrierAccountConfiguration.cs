using Courier.International.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.International.Infrastructure;

public class CarrierAccountConfiguration : IEntityTypeConfiguration<CarrierAccount>
{
    public void Configure(EntityTypeBuilder<CarrierAccount> builder)
    {
        builder.ToTable("CarrierAccounts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.CarrierCode).HasMaxLength(50).IsRequired();
        builder.Property(a => a.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(a => a.WebhookPublicId).HasMaxLength(64).IsRequired();
        builder.Property(a => a.CreatedAt).HasDefaultValueSql("now()");

        builder.HasIndex(a => a.OrganizationId);
        // Platform-unique: the anonymous webhook route resolves the account (and so the tenant) by it.
        builder.HasIndex(a => a.WebhookPublicId).IsUnique();
    }
}
