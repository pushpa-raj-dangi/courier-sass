using Courier.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Billing.Infrastructure;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("Invoices");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.PlanNameSnapshot).HasMaxLength(150).IsRequired();
        builder.Property(i => i.AmountNprSnapshot).HasPrecision(12, 2);
        builder.HasIndex(i => new { i.OrganizationId, i.Status });
        // One invoice per (Subscription, CycleStart) — the generation job's own query already
        // filters on CurrentCycleEnd, but this index makes "did we already bill this cycle"
        // DB-enforced too, turning a would-be double-run of the job (e.g. Hangfire retry racing
        // a slow first run) into a caught DbUpdateException instead of a duplicate charge.
        builder.HasIndex(i => new { i.SubscriptionId, i.CycleStart }).IsUnique();
    }
}
