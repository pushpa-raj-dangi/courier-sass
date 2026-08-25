using Courier.Cod.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Cod.Infrastructure;

public class AdjustmentConfiguration : IEntityTypeConfiguration<Adjustment>
{
    public void Configure(EntityTypeBuilder<Adjustment> builder)
    {
        builder.ToTable("Adjustments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Reason).HasMaxLength(1000).IsRequired();
        builder.Property(a => a.BeforeValue).HasPrecision(18, 2);
        builder.Property(a => a.AfterValue).HasPrecision(18, 2);

        builder.HasIndex(a => a.OrganizationId);
        builder.HasIndex(a => new { a.EntityType, a.EntityId });
    }
}
