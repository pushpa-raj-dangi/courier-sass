using Courier.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Notifications.Infrastructure;

public class NotificationLogConfiguration : IEntityTypeConfiguration<NotificationLog>
{
    public void Configure(EntityTypeBuilder<NotificationLog> builder)
    {
        builder.ToTable("NotificationLogs");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.EventType).HasMaxLength(100).IsRequired();
        builder.Property(l => l.RecipientAddress).HasMaxLength(320); // RFC 5321 max email length; generous enough for a phone number too.

        builder.HasIndex(l => new { l.OrganizationId, l.SentAt });
    }
}
