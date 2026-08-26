using Courier.Webhooks.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Webhooks.Infrastructure;

public class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> builder)
    {
        builder.ToTable("WebhookDeliveries");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.EventType).HasMaxLength(100).IsRequired();
        builder.Property(d => d.Payload).IsRequired();
        builder.Property(d => d.ResponseSnippet).HasMaxLength(2000);

        // At most one WebhookDelivery per (subscription, source event) — DB-enforced idempotency
        // for the consumer's delivery-row creation (Task 11), not just a checked-then-written
        // pre-check. Same TOCTOU-guard lesson as Phase 5's RateCard versioning and Phase 10's
        // FinancialTransaction uniqueness.
        builder.HasIndex(d => new { d.WebhookSubscriptionId, d.SourceEventId }).IsUnique();
        // The retry job's (Task 12) hot path: "every delivery whose NextRetryAt is due."
        builder.HasIndex(d => d.NextRetryAt).HasFilter("\"NextRetryAt\" IS NOT NULL");

        builder.HasOne<WebhookSubscription>().WithMany()
            .HasForeignKey(d => d.WebhookSubscriptionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
