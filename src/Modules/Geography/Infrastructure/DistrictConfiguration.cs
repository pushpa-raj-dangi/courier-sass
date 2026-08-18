using Courier.Geography.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Geography.Infrastructure;

public class DistrictConfiguration : IEntityTypeConfiguration<District>
{
    public void Configure(EntityTypeBuilder<District> builder)
    {
        builder.ToTable("Districts");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(d => d.ProvinceId);

        builder.HasOne<Province>()
            .WithMany()
            .HasForeignKey(d => d.ProvinceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasData(GeographySeedData.Districts.Select(d => new District
        {
            Id = GeographyIds.DistrictId(d.Province, d.District),
            ProvinceId = GeographyIds.ProvinceId(d.Province),
            Name = d.District
        }));
    }
}
