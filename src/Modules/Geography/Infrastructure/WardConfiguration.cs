using Courier.Geography.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Geography.Infrastructure;

public class WardConfiguration : IEntityTypeConfiguration<Ward>
{
    public void Configure(EntityTypeBuilder<Ward> builder)
    {
        builder.ToTable("Wards");
        builder.HasKey(w => w.Id);
        builder.HasIndex(w => w.LocalLevelId);
        builder.HasIndex(w => new { w.LocalLevelId, w.Number }).IsUnique();

        builder.HasOne<LocalLevel>()
            .WithMany()
            .HasForeignKey(w => w.LocalLevelId)
            .OnDelete(DeleteBehavior.Restrict);

        var wards = new List<Ward>();
        foreach (var l in GeographySeedData.LocalLevels)
        {
            var localLevelId = GeographyIds.LocalLevelId(l.Province, l.District, l.LocalLevel);
            foreach (var number in GeographySeedData.WardNumbers)
            {
                wards.Add(new Ward
                {
                    Id = GeographyIds.WardId(l.Province, l.District, l.LocalLevel, number),
                    LocalLevelId = localLevelId,
                    Number = number
                });
            }
        }
        builder.HasData(wards);
    }
}
