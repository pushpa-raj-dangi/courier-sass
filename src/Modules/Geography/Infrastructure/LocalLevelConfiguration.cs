using Courier.Geography.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Geography.Infrastructure;

public class LocalLevelConfiguration : IEntityTypeConfiguration<LocalLevel>
{
    public void Configure(EntityTypeBuilder<LocalLevel> builder)
    {
        builder.ToTable("LocalLevels");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Name).HasMaxLength(150).IsRequired();
        builder.HasIndex(l => l.DistrictId);

        builder.HasOne<District>()
            .WithMany()
            .HasForeignKey(l => l.DistrictId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasData(GeographySeedData.LocalLevels.Select(l => new LocalLevel
        {
            Id = GeographyIds.LocalLevelId(l.Province, l.District, l.LocalLevel),
            DistrictId = GeographyIds.DistrictId(l.Province, l.District),
            Name = l.LocalLevel,
            Type = l.Type
        }));
    }
}
