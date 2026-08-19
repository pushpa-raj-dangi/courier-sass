using Courier.Merchants.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Merchants.Infrastructure;

public class MerchantKycDocumentConfiguration : IEntityTypeConfiguration<MerchantKycDocument>
{
    public void Configure(EntityTypeBuilder<MerchantKycDocument> builder)
    {
        builder.ToTable("MerchantKycDocuments");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.StorageKey).HasMaxLength(500).IsRequired();

        builder.HasIndex(d => d.OrganizationId);
        builder.HasIndex(d => d.MerchantId);

        builder.HasOne<Merchant>()
            .WithMany()
            .HasForeignKey(d => d.MerchantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
