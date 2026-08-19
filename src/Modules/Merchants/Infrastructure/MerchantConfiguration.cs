using Courier.Merchants.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Merchants.Infrastructure;

public class MerchantConfiguration : IEntityTypeConfiguration<Merchant>
{
    public void Configure(EntityTypeBuilder<Merchant> builder)
    {
        builder.ToTable("Merchants");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.LegalName).HasMaxLength(200).IsRequired();
        builder.Property(m => m.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(m => m.ContactPhone).HasMaxLength(30);
        builder.Property(m => m.ContactEmail).HasMaxLength(200);

        // Every tenant-filtered query hits this column.
        builder.HasIndex(m => m.OrganizationId);

        // No FK to Organization here: Merchants has no project reference to Identity. That FK
        // is configured centrally in Persistence/AppDbContext.OnModelCreating instead, mirroring
        // Branch.OrganizationId/DeliveryZone.OrganizationId's identical reasoning.
    }
}
