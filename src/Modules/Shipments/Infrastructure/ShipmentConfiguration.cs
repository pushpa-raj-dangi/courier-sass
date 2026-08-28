using Courier.Branches.Domain;
using Courier.Geography.Domain;
using Courier.Merchants.Domain;
using Courier.Pricing.Domain;
using Courier.Shipments.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Shipments.Infrastructure;

public class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.ToTable("Shipments");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(s => s.Awb).HasMaxLength(30).IsRequired();
        builder.Property(s => s.RecipientName).HasMaxLength(200).IsRequired();
        builder.Property(s => s.RecipientPhone).HasMaxLength(30).IsRequired();
        builder.Property(s => s.RecipientAddressLine).HasMaxLength(500).IsRequired();
        builder.Property(s => s.SenderName).HasMaxLength(200).IsRequired();
        builder.Property(s => s.SenderPhone).HasMaxLength(30).IsRequired();
        builder.Property(s => s.SenderAddressLine).HasMaxLength(500).IsRequired();

        builder.Property(s => s.WeightKg).HasPrecision(10, 3);
        builder.Property(s => s.LengthCm).HasPrecision(10, 2);
        builder.Property(s => s.WidthCm).HasPrecision(10, 2);
        builder.Property(s => s.HeightCm).HasPrecision(10, 2);
        builder.Property(s => s.DeclaredValue).HasPrecision(18, 2);
        builder.Property(s => s.CodAmount).HasPrecision(18, 2);
        builder.Property(s => s.DeliveryCharge).HasPrecision(18, 2);
        builder.Property(s => s.InsuranceCharge).HasPrecision(18, 2);
        builder.Property(s => s.OtherCharges).HasPrecision(18, 2);
        builder.Property(s => s.TotalCharge).HasPrecision(18, 2);

        // PostgreSQL-native optimistic concurrency token — no new column, no schema change. Guards
        // TransitionAsync/CancelAsync's read-check-write against two concurrent identical requests
        // (e.g. two racing POST /{id}/deliver calls) both passing the expectedFrom check and both
        // succeeding, which would append two ShipmentEvent rows for one physical operation and
        // corrupt the append-only event history's integrity (CLAUDE.md §11/§39).
        builder.Property<uint>("xmin").IsRowVersion();

        // Every tenant-filtered query hits this column.
        builder.HasIndex(s => s.OrganizationId);
        // AWB is unique per org (DOMAIN-MODEL.md) — this is also the index
        // ShipmentService.GenerateAwbAsync's collision-retry relies on catching a violation of.
        builder.HasIndex(s => new { s.OrganizationId, s.Awb }).IsUnique();
        builder.HasIndex(s => s.MerchantId);

        // Cross-module FKs: Shipments has project references to Branches/Merchants/Pricing/
        // Geography (Task 1), so these are configured directly here, mirroring how
        // Merchants/Branches configure their own Geography FKs.
        builder.HasOne<Branch>().WithMany().HasForeignKey(s => s.OriginBranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Branch>().WithMany().HasForeignKey(s => s.DestinationBranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Branch>().WithMany().HasForeignKey(s => s.CurrentBranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Merchant>().WithMany().HasForeignKey(s => s.MerchantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ServiceType>().WithMany().HasForeignKey(s => s.ServiceTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Province>().WithMany().HasForeignKey(s => s.RecipientProvinceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<District>().WithMany().HasForeignKey(s => s.RecipientDistrictId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LocalLevel>().WithMany().HasForeignKey(s => s.RecipientLocalLevelId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Province>().WithMany().HasForeignKey(s => s.SenderProvinceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<District>().WithMany().HasForeignKey(s => s.SenderDistrictId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LocalLevel>().WithMany().HasForeignKey(s => s.SenderLocalLevelId).OnDelete(DeleteBehavior.Restrict);

        // No FK to Organization/User here: Shipments has no project reference to Identity —
        // wired centrally in Persistence/AppDbContext.OnModelCreating instead (Task 9).
        // CurrentCustodianUserId is likewise left unconstrained at the DB level for the same
        // reason (a real FK to Users would require the same central-wiring treatment; deferred
        // since nothing in this phase queries through it, only stores/clears it).
    }
}
