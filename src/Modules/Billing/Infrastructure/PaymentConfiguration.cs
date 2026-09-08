using Courier.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Billing.Infrastructure;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.AmountNpr).HasPrecision(12, 2);
        builder.Property(p => p.RawVerificationPayload).HasColumnType("jsonb");
        // Payment isolates transitively through InvoiceId -> Invoice.OrganizationId — same
        // "child table, no own OrganizationId column" pattern as ShipmentItem/ManifestShipment.
        builder.HasIndex(p => p.InvoiceId);
    }
}
