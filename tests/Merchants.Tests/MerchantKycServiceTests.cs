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

file class CapturingAuditLogger : IAuditLogger
{
    public List<AuditEntry> Entries { get; } = new();
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }
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

    private static async Task<(TestDbContext Db, MerchantKycService Service, Guid MerchantId, Guid DocumentId, List<AuditEntry> Audit)> SeedUploadedAsync()
    {
        var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var user = await SeedMerchantUserAsync(dbContext, merchantId, organizationId);
        var audit = new CapturingAuditLogger();
        var service = new MerchantKycService(dbContext, new FixedTenantProvider(organizationId), audit, new InMemoryObjectStorage());
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("bytes"));
        var uploaded = await service.UploadDocumentAsync(KycDocumentType.PanCard, content, "application/pdf", MerchantPrincipal(user.Id, merchantId));
        return (dbContext, service, merchantId, uploaded.Id, audit.Entries);
    }

    [Fact]
    public async Task ListDocumentsAsync_OrdersLegacyNullsFirstThenByUploadedAtThenId()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var user = await SeedMerchantUserAsync(dbContext, merchantId, organizationId);
        var t0 = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        MerchantKycDocument Doc(string id, DateTime? uploadedAt) => new()
        {
            Id = Guid.Parse(id),
            MerchantId = merchantId,
            OrganizationId = organizationId,
            DocumentType = KycDocumentType.PanCard,
            StorageKey = $"kyc/{id}",
            UploadedAt = uploadedAt
        };
        dbContext.Set<MerchantKycDocument>().AddRange(
            Doc("00000000-0000-0000-0000-000000000005", t0.AddDays(2)),
            Doc("00000000-0000-0000-0000-000000000004", t0),
            Doc("00000000-0000-0000-0000-000000000003", null),
            Doc("00000000-0000-0000-0000-000000000002", t0),
            Doc("00000000-0000-0000-0000-000000000001", null),
            new MerchantKycDocument
            {
                Id = Guid.NewGuid(), MerchantId = Guid.NewGuid(), OrganizationId = organizationId,
                DocumentType = KycDocumentType.PanCard, StorageKey = "kyc/other-merchant", UploadedAt = t0
            });
        await dbContext.SaveChangesAsync();
        var service = new MerchantKycService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new InMemoryObjectStorage());

        var result = await service.ListDocumentsAsync(MerchantPrincipal(user.Id, merchantId));

        Assert.Equal(
            new[] { 1, 3, 2, 4, 5 }.Select(n => Guid.Parse($"00000000-0000-0000-0000-00000000000{n}")),
            result.Select(d => d.Id));
    }

    [Fact]
    public async Task UploadDocumentAsync_SetsUploadedAt()
    {
        var (db, _, _, documentId, _) = await SeedUploadedAsync();
        await using var scope = db;

        var row = await db.Set<MerchantKycDocument>().SingleAsync(d => d.Id == documentId);
        Assert.NotNull(row.UploadedAt);
        Assert.True(Math.Abs((DateTime.UtcNow - row.UploadedAt!.Value).TotalSeconds) < 5);
        Assert.Null(row.ReviewedAt);
        Assert.Null(row.RejectionReason);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task ReviewDocumentAsync_RejectWithoutReason_Throws(string? reason)
    {
        var (db, service, merchantId, documentId, _) = await SeedUploadedAsync();
        await using var scope = db;

        await Assert.ThrowsAsync<MerchantValidationException>(() => service.ReviewDocumentAsync(
            merchantId, documentId, new ReviewKycDocumentRequest(KycStatus.Rejected, reason), MerchantPrincipal(Guid.NewGuid(), merchantId)));

        var row = await db.Set<MerchantKycDocument>().SingleAsync(d => d.Id == documentId);
        Assert.Equal(KycStatus.Pending, row.Status);
    }

    [Fact]
    public async Task ReviewDocumentAsync_RejectReasonOver500_Throws()
    {
        var (db, service, merchantId, documentId, _) = await SeedUploadedAsync();
        await using var scope = db;

        await Assert.ThrowsAsync<MerchantValidationException>(() => service.ReviewDocumentAsync(
            merchantId, documentId, new ReviewKycDocumentRequest(KycStatus.Rejected, new string('x', 501)), MerchantPrincipal(Guid.NewGuid(), merchantId)));
    }

    [Fact]
    public async Task ReviewDocumentAsync_RejectWithReason_StoresTrimmedReasonReviewedAtAndAudit()
    {
        var (db, service, merchantId, documentId, audit) = await SeedUploadedAsync();
        await using var scope = db;

        var result = await service.ReviewDocumentAsync(
            merchantId, documentId, new ReviewKycDocumentRequest(KycStatus.Rejected, "  Photo is blurry  "), MerchantPrincipal(Guid.NewGuid(), merchantId));

        Assert.Equal("Photo is blurry", result!.RejectionReason);
        Assert.NotNull(result.ReviewedAt);
        var row = await db.Set<MerchantKycDocument>().SingleAsync(d => d.Id == documentId);
        Assert.Equal("Photo is blurry", row.RejectionReason);
        Assert.NotNull(row.ReviewedAt);
        var entry = Assert.Single(audit, e => e.Action == "merchant_kyc.reviewed");
        Assert.Contains("Pending", entry.Before);
        Assert.Contains("Rejected", entry.After);
        Assert.Contains("Photo is blurry", entry.After);
    }

    [Fact]
    public async Task ReviewDocumentAsync_ApproveAfterReject_ClearsReason()
    {
        var (db, service, merchantId, documentId, _) = await SeedUploadedAsync();
        await using var scope = db;
        var principal = MerchantPrincipal(Guid.NewGuid(), merchantId);
        var rejected = await service.ReviewDocumentAsync(
            merchantId, documentId, new ReviewKycDocumentRequest(KycStatus.Rejected, "Blurry"), principal);

        var approved = await service.ReviewDocumentAsync(
            merchantId, documentId, new ReviewKycDocumentRequest(KycStatus.Approved, "ignored"), principal);

        Assert.Equal(KycStatus.Approved, approved!.Status);
        Assert.Null(approved.RejectionReason);
        Assert.True(approved.ReviewedAt >= rejected!.ReviewedAt);
    }
}
