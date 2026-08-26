using Courier.Events.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Events.Infrastructure;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.EventType).HasMaxLength(100).IsRequired();
        builder.Property(m => m.Payload).IsRequired();

        builder.HasIndex(m => m.OrganizationId);
        // The outbox publisher job (Task 4) polls exactly this shape: unpublished rows, oldest
        // first. A partial index (WHERE "PublishedAt" IS NULL) keeps that scan cheap forever
        // regardless of how many already-published rows accumulate.
        builder.HasIndex(m => m.CreatedAt).HasFilter("\"PublishedAt\" IS NULL");
    }
}
