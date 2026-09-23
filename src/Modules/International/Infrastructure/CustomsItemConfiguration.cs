using Courier.International.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.International.Infrastructure;

public class CustomsItemConfiguration : IEntityTypeConfiguration<CustomsItem>
{
    public void Configure(EntityTypeBuilder<CustomsItem> builder)
    {
        builder.ToTable("CustomsItems");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.HsCode).HasMaxLength(20).IsRequired();
        builder.Property(i => i.CustomsValueAmount).HasPrecision(18, 2);
        builder.Property(i => i.CustomsValueCurrency).HasMaxLength(3).IsRequired();

        builder.HasIndex(i => i.InternationalLegId);
        builder.HasOne<InternationalLeg>().WithMany().HasForeignKey(i => i.InternationalLegId).OnDelete(DeleteBehavior.Cascade);
    }
}
