using Courier.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Identity.Infrastructure;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Email).HasMaxLength(320).IsRequired();
        builder.Property(u => u.PasswordHash).IsRequired();
        builder.Property(u => u.FullName).HasMaxLength(200).IsRequired();
        builder.HasIndex(u => u.Email).IsUnique();

        // Every tenant-filtered query hits this column.
        builder.HasIndex(u => u.OrganizationId);

        // FK-only relationship (no navigation property on the entity, keeping the domain model
        // free of persistence concerns). Restrict, so deleting an Organization that still owns
        // Users fails loudly instead of silently cascading real accounts away.
        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(u => u.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
