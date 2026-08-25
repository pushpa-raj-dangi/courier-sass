using Courier.Cod.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Cod.Infrastructure;

public class FinancialTransactionConfiguration : IEntityTypeConfiguration<FinancialTransaction>
{
    public void Configure(EntityTypeBuilder<FinancialTransaction> builder)
    {
        builder.ToTable("FinancialTransactions");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Amount).HasPrecision(18, 2);
        builder.Property(f => f.ReferenceEntityType).HasMaxLength(100).IsRequired();

        builder.HasIndex(f => f.OrganizationId);
        // Unique, not just a lookup index: enforces "at most one FinancialTransaction per
        // (ReferenceEntityType, ReferenceEntityId)" at the database — CreateFinanceVerificationAsync
        // (Task 9) also does a pre-check AnyAsync for a clean error message in the common case, but
        // a bare pre-check is a TOCTOU race under concurrent requests (Phase 5's exact rate-card
        // versioning lesson, commit a83c320) — only this constraint, backed by a caught
        // DbUpdateException on the actual insert, makes "verify the same BranchCashSettlement twice"
        // truly impossible under concurrency. Proven by Task 14's Postgres concurrency test.
        builder.HasIndex(f => new { f.ReferenceEntityType, f.ReferenceEntityId }).IsUnique();
    }
}
