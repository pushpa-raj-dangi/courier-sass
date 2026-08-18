using Courier.Geography.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Geography.Infrastructure;

public class AreaConfiguration : IEntityTypeConfiguration<Area>
{
    public void Configure(EntityTypeBuilder<Area> builder)
    {
        builder.ToTable("Areas");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Name).HasMaxLength(150).IsRequired();
        builder.HasIndex(a => a.WardId);

        builder.HasOne<Ward>()
            .WithMany()
            .HasForeignKey(a => a.WardId)
            .OnDelete(DeleteBehavior.Restrict);

        var areas = new List<Area>();
        foreach (var l in GeographySeedData.LocalLevels)
        {
            foreach (var number in GeographySeedData.WardNumbers)
            {
                var wardId = GeographyIds.WardId(l.Province, l.District, l.LocalLevel, number);
                foreach (var label in GeographySeedData.AreaLabels)
                {
                    areas.Add(new Area
                    {
                        Id = GeographyIds.AreaId(l.Province, l.District, l.LocalLevel, number, label),
                        WardId = wardId,
                        Name = $"{l.LocalLevel} Ward {number} {label}"
                    });
                }
            }
        }
        builder.HasData(areas);
    }
}
