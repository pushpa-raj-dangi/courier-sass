using Courier.Riders.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Riders.Infrastructure;

public class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.ToTable("Vehicles");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Type).HasMaxLength(50).IsRequired();
        builder.Property(v => v.PlateNumber).HasMaxLength(30).IsRequired();
        builder.Property(v => v.Capacity).HasPrecision(10, 2);

        builder.HasIndex(v => v.OrganizationId);
        builder.HasIndex(v => v.RiderId);

        builder.HasOne<Rider>().WithMany().HasForeignKey(v => v.RiderId).OnDelete(DeleteBehavior.SetNull);
    }
}
