using Courier.Shipments.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Shipments.Infrastructure;

public class PickupRequestConfiguration : IEntityTypeConfiguration<PickupRequest>
{
    public void Configure(EntityTypeBuilder<PickupRequest> builder)
    {
        builder.ToTable("PickupRequests");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Notes).HasMaxLength(1000);
        // Guid[] maps to a native Postgres uuid[] column, same as WebhookSubscription.Events'
        // string[] -> text[] — no HasColumnType needed, Npgsql's EF Core provider handles both.

        builder.HasIndex(p => p.OrganizationId);
        builder.HasIndex(p => p.MerchantId);
        builder.HasIndex(p => p.BranchId);
    }
}
