using Courier.Pricing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Pricing.Infrastructure;

public class FxRateConfiguration : IEntityTypeConfiguration<FxRate>
{
    public void Configure(EntityTypeBuilder<FxRate> builder)
    {
        builder.ToTable("FxRates", t =>
        {
            t.HasCheckConstraint("CK_FxRates_Rate_Positive", "\"Rate\" > 0");
            t.HasCheckConstraint("CK_FxRates_Distinct_Currencies", "\"FromCurrency\" <> \"ToCurrency\"");
        });
        builder.HasKey(r => r.Id);
        builder.Property(r => r.FromCurrency).HasMaxLength(3).IsRequired();
        builder.Property(r => r.ToCurrency).HasMaxLength(3).IsRequired();
        builder.Property(r => r.Rate).HasPrecision(18, 8);
        builder.Property(r => r.Source).HasMaxLength(50).IsRequired();
        builder.HasIndex(r => new { r.FromCurrency, r.ToCurrency, r.EffectiveAt });
    }
}
