using Courier.Merchants.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Merchants.Infrastructure;

public class MerchantRefreshTokenConfiguration : IEntityTypeConfiguration<MerchantRefreshToken>
{
    public void Configure(EntityTypeBuilder<MerchantRefreshToken> builder)
    {
        builder.ToTable("MerchantRefreshTokens");
        builder.HasKey(rt => rt.Id);
        builder.Property(rt => rt.TokenHash).IsRequired();
        builder.HasIndex(rt => rt.TokenHash).IsUnique();

        // Tokens follow the merchant user: deleting a MerchantUser should take their refresh
        // tokens with it rather than leaving credentials that resolve to a missing account.
        // Mirrors RefreshTokenConfiguration's identical Cascade choice for User/RefreshToken.
        builder.HasOne<MerchantUser>()
            .WithMany()
            .HasForeignKey(rt => rt.MerchantUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
