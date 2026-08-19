using System.Security.Claims;
using System.Text;
using Courier.Audit.Application;
using Courier.Merchants.Application;
using Courier.Merchants.Domain;
using Courier.Merchants.Infrastructure;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Merchants.Tests;

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class InMemoryObjectStorage : IObjectStorage
{
    public Task<string> UploadAsync(string keyPrefix, Stream content, string contentType, CancellationToken cancellationToken = default)
        => Task.FromResult($"{keyPrefix}/{Guid.NewGuid():N}");

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult<Stream>(new MemoryStream());
}

public class MerchantKycServiceTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new MerchantUserConfiguration());
            modelBuilder.ApplyConfiguration(new MerchantKycDocumentConfiguration());
        }
    }

    private static TestDbContext CreateContext() => new(new DbContextOptionsBuilder<TestDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static ClaimsPrincipal MerchantPrincipal(Guid userId, Guid merchantId) => new(new ClaimsIdentity(new[]
    {
        new Claim("sub", userId.ToString()),
        new Claim("merchant_id", merchantId.ToString())
    }, "test"));

    private static async Task<MerchantUser> SeedMerchantUserAsync(TestDbContext dbContext, Guid merchantId, Guid organizationId)
    {
        var user = new MerchantUser
        {
            Id = Guid.NewGuid(),
            MerchantId = merchantId,
            OrganizationId = organizationId,
            Email = $"user-{Guid.NewGuid():N}@example.com",
            PasswordHash = "not-used",
            FullName = "Merchant User",
            Role = MerchantUserRole.Owner,
            IsActive = true
        };
        dbContext.Set<MerchantUser>().Add(user);
        await dbContext.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task UploadDocumentAsync_PersistsPendingDocumentUnderCorrectMerchant()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var user = await SeedMerchantUserAsync(dbContext, merchantId, organizationId);
        var service = new MerchantKycService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new InMemoryObjectStorage());
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("fake pan card bytes"));

        var result = await service.UploadDocumentAsync(KycDocumentType.PanCard, content, "application/pdf", MerchantPrincipal(user.Id, merchantId));

        Assert.Equal(KycStatus.Pending, result.Status);
        Assert.Null(result.ReviewedByUserId);
        var row = await dbContext.Set<MerchantKycDocument>().SingleAsync(d => d.Id == result.Id);
        Assert.Equal(merchantId, row.MerchantId);
        Assert.Equal(organizationId, row.OrganizationId);
    }

    [Fact]
    public async Task ReviewDocumentAsync_ApprovesAndRecordsReviewer()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var user = await SeedMerchantUserAsync(dbContext, merchantId, organizationId);
        var uploadService = new MerchantKycService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new InMemoryObjectStorage());
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("fake citizenship bytes"));
        var uploaded = await uploadService.UploadDocumentAsync(KycDocumentType.CitizenshipDocument, content, "application/pdf", MerchantPrincipal(user.Id, merchantId));

        var reviewerId = Guid.NewGuid();
        var reviewed = await uploadService.ReviewDocumentAsync(
            merchantId, uploaded.Id, new ReviewKycDocumentRequest(KycStatus.Approved), MerchantPrincipal(reviewerId, merchantId));

        Assert.NotNull(reviewed);
        Assert.Equal(KycStatus.Approved, reviewed!.Status);
        Assert.Equal(reviewerId, reviewed.ReviewedByUserId);
    }

    [Fact]
    public async Task ReviewDocumentAsync_WrongOrganization_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var user = await SeedMerchantUserAsync(dbContext, merchantId, organizationId);
        var uploadService = new MerchantKycService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new InMemoryObjectStorage());
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("fake bytes"));
        var uploaded = await uploadService.UploadDocumentAsync(KycDocumentType.Other, content, "application/pdf", MerchantPrincipal(user.Id, merchantId));

        var wrongOrgService = new MerchantKycService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NoopAuditLogger(), new InMemoryObjectStorage());
        var result = await wrongOrgService.ReviewDocumentAsync(
            merchantId, uploaded.Id, new ReviewKycDocumentRequest(KycStatus.Approved), MerchantPrincipal(Guid.NewGuid(), merchantId));

        Assert.Null(result);
    }
}
