using Courier.Geography.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Geography.Infrastructure;

public class DeliveryZoneConfiguration : IEntityTypeConfiguration<DeliveryZone>
{
    public void Configure(EntityTypeBuilder<DeliveryZone> builder)
    {
        builder.ToTable("DeliveryZones");
        builder.HasKey(z => z.Id);
        builder.Property(z => z.Name).HasMaxLength(150).IsRequired();

        // Every tenant-filtered query hits this column.
        builder.HasIndex(z => z.OrganizationId);

        builder.HasMany(z => z.CoveredAreas).WithMany()
            .UsingEntity<Dictionary<string, object>>(
                "DeliveryZoneAreas",
                j => j.HasOne<Area>().WithMany().HasForeignKey("CoveredAreasId").OnDelete(DeleteBehavior.Restrict),
                j => j.HasOne<DeliveryZone>().WithMany().HasForeignKey("DeliveryZoneId"),
                j => j.ToTable("DeliveryZoneAreas"));

        // No FK to Organizations here: Geography has no project reference to Identity, same
        // reasoning as Branch.OrganizationId (see Persistence/AppDbContext.OnModelCreating,
        // wired centrally there instead).
    }
}
