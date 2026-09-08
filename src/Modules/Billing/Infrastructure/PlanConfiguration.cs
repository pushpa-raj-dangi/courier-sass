using Courier.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Billing.Infrastructure;

public class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.ToTable("Plans");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).HasMaxLength(150).IsRequired();
        builder.Property(p => p.Slug).HasMaxLength(150).IsRequired();
        builder.Property(p => p.MonthlyPriceNpr).HasPrecision(12, 2);
        builder.HasIndex(p => p.Slug).IsUnique();
        // At most one default Plan at a time — PlanService.CreatePlanAsync enforces this by
        // clearing any existing IsDefault before setting a new one inside the same
        // SaveChangesAsync (no DB-level partial-unique-index race guard here: unlike RateCard's
        // per-org active-card race, this is a SuperAdmin-only, low-frequency, single-operator
        // action — the read-then-write window is not a realistic concurrency risk).
    }
}
