using Courier.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Billing.Infrastructure;

public class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("Subscriptions");
        builder.HasKey(s => s.Id);
        // One Subscription per org — enforced here, not just in application code, since a
        // second row for the same org would make "the org's current plan" ambiguous.
        builder.HasIndex(s => s.OrganizationId).IsUnique();
    }
}
