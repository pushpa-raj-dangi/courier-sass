using Courier.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Platform.Infrastructure;

public class PlatformSettingDefinitionConfiguration : IEntityTypeConfiguration<PlatformSettingDefinition>
{
    public void Configure(EntityTypeBuilder<PlatformSettingDefinition> builder)
    {
        builder.ToTable("PlatformSettingDefinitions");
        builder.HasKey(d => d.Key);
        builder.Property(d => d.Key).HasMaxLength(100);
        builder.Property(d => d.Category).HasMaxLength(50).IsRequired();
        builder.Property(d => d.ConfigPath).HasMaxLength(150).IsRequired();
        builder.Property(d => d.ValueType).HasMaxLength(20).IsRequired();
        builder.Property(d => d.Description).HasMaxLength(500);
        builder.HasData(PlatformSettingCatalog.Definitions);
    }
}
