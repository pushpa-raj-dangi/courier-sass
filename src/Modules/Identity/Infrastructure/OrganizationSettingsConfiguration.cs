using Courier.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Identity.Infrastructure;

public class OrganizationSettingsConfiguration : IEntityTypeConfiguration<OrganizationSettings>
{
    public void Configure(EntityTypeBuilder<OrganizationSettings> builder)
    {
        builder.ToTable("OrganizationSettings");
        builder.HasKey(s => s.OrganizationId);
        builder.Property(s => s.TimeZoneId).HasMaxLength(100).IsRequired();
        builder.Property(s => s.DefaultLocale).HasMaxLength(10).IsRequired().HasDefaultValue(OrganizationLocaleOptions.DefaultLocale);
        builder.Property(s => s.CalendarPreference).HasMaxLength(20).IsRequired().HasDefaultValue(OrganizationLocaleOptions.DefaultCalendar);
        builder.Property(s => s.SupportEmail).HasMaxLength(320);
        builder.Property(s => s.SupportPhone).HasMaxLength(30);
        builder.Property(s => s.LogoUrl).HasMaxLength(2000);

        builder.HasOne<Organization>()
            .WithOne()
            .HasForeignKey<OrganizationSettings>(s => s.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
