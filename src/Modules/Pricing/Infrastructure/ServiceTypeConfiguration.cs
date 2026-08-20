using Courier.Pricing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Pricing.Infrastructure;

public class ServiceTypeConfiguration : IEntityTypeConfiguration<ServiceType>
{
    public void Configure(EntityTypeBuilder<ServiceType> builder)
    {
        builder.ToTable("ServiceTypes");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).HasMaxLength(50).IsRequired();
        builder.HasIndex(s => s.Code).IsUnique();

        builder.HasData(PricingSeedData.ServiceTypes.Select(s => new ServiceType
        {
            Id = PricingIds.ServiceTypeId(s.Code),
            Code = s.Code,
            Name = s.Name
        }));

        // No tenant filter: shared reference data, identical across every tenant, seeded once
        // (mirrors Geography's Province/District — ARCHITECTURE.md §2).
    }
}
