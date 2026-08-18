using Courier.Identity.Application;
using Courier.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Identity.Infrastructure;

public class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("Permissions");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Key).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(300).IsRequired();
        builder.HasIndex(p => p.Key).IsUnique();

        builder.HasData(PermissionCatalog.All.Select(p => new Permission
        {
            Id = PermissionCatalog.IdFor(p.Key),
            Key = p.Key,
            Description = p.Description
        }));
    }
}
