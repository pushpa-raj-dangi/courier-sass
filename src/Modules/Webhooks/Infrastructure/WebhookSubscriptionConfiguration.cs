using Courier.Webhooks.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Webhooks.Infrastructure;

public class WebhookSubscriptionConfiguration : IEntityTypeConfiguration<WebhookSubscription>
{
    public void Configure(EntityTypeBuilder<WebhookSubscription> builder)
    {
        builder.ToTable("WebhookSubscriptions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.TargetUrl).HasMaxLength(2048).IsRequired();
        builder.Property(s => s.Secret).IsRequired();

        builder.HasIndex(s => s.OrganizationId);
        builder.HasIndex(s => s.MerchantId);
        // The Webhooks consumer's (Task 11) per-message hot path: "every active subscription in
        // this org whose Events contains this event type."
        builder.HasIndex(s => new { s.OrganizationId, s.IsActive });
    }
}
