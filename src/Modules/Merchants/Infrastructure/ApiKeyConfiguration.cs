using Courier.Merchants.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Merchants.Infrastructure;

public class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("ApiKeys");
        builder.HasKey(k => k.Id);
        builder.Property(k => k.HashedKey).IsRequired();
        builder.Property(k => k.Prefix).HasMaxLength(16).IsRequired();

        builder.HasIndex(k => k.OrganizationId);
        builder.HasIndex(k => k.MerchantId);
        // ApiKeyAuthenticationHandler's (Task 5) hot path: look up the one active key row for a
        // presented prefix. Unique because a prefix collision would make lookup ambiguous —
        // ApiKeyService (Task 3) retries generation on collision.
        builder.HasIndex(k => k.Prefix).IsUnique();

        builder.HasOne<Merchant>()
            .WithMany()
            .HasForeignKey(k => k.MerchantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
