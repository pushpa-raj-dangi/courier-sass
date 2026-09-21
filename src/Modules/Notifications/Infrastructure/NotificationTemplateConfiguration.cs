using Courier.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Notifications.Infrastructure;

public class NotificationTemplateConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationTemplate> builder)
    {
        builder.ToTable("NotificationTemplates");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.EventType).HasMaxLength(100).IsRequired();
        builder.Property(t => t.Body).IsRequired();
        builder.Property(t => t.Locale).HasMaxLength(10).IsRequired().HasDefaultValue(NotificationLocales.Default);

        builder.HasIndex(t => t.OrganizationId);
        // The lookup NotificationDispatchService performs every dispatch: "active template for
        // this org/event/channel/locale." Not unique — an org could plausibly have more than one
        // inactive historical row per combination in a future template-editing flow — but this
        // index makes the active-row lookup itself cheap regardless.
        builder.HasIndex(t => new { t.OrganizationId, t.EventType, t.Channel, t.Locale });
    }
}
