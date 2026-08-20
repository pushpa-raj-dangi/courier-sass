using Courier.Pricing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Pricing.Infrastructure;

public class RateCardConfiguration : IEntityTypeConfiguration<RateCard>
{
    public void Configure(EntityTypeBuilder<RateCard> builder)
    {
        builder.ToTable("RateCards");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).HasMaxLength(150).IsRequired();

        // Every tenant-filtered query hits this column.
        builder.HasIndex(r => r.OrganizationId);
        // RateCardService.CreateRateCardAsync queries "the org's current IsActive card" — this
        // index makes that lookup and the "at most one active per org" check both cheap.
        builder.HasIndex(r => new { r.OrganizationId, r.IsActive });
        // DB-enforced "at most one active RateCard per org" (CLAUDE.md §16's versioning
        // invariant) — RateCardService.CreateRateCardAsync's read-then-write of the previous
        // active card is otherwise a TOCTOU race under concurrent requests; this filtered unique
        // index turns a would-be silent double-active-card corruption into a caught
        // DbUpdateException there.
        builder.HasIndex(r => r.OrganizationId)
            .IsUnique()
            .HasFilter("\"IsActive\" = true")
            .HasDatabaseName("IX_RateCards_OrganizationId_ActiveUnique");

        // No FK to Organization here: Pricing has no project reference to Identity — wired
        // centrally in Persistence/AppDbContext.OnModelCreating instead (Task 9).
    }
}
