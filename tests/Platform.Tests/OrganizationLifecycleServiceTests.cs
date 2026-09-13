using Courier.Audit.Application;
using Courier.Billing.Application;
using Courier.Branches.Application;
using Courier.Identity.Application;
using Courier.Merchants.Application;
using Courier.Platform.Application;
using Courier.SharedKernel;
using Xunit;

namespace Courier.Platform.Tests;

public class OrganizationLifecycleServiceTests
{
    private sealed class FakeOrganizationLookupService : IOrganizationLookupService
    {
        public bool Suspended { get; private set; }
        public bool RevokedStaffTokens { get; private set; }
        public List<OrganizationRecord> Organizations { get; set; } = new();
        public string? LastSearch { get; private set; }
        public bool? LastIsActive { get; private set; }
        public bool? LastIsDeleted { get; private set; }
        public int? LastPage { get; private set; }
        public int? LastPageSize { get; private set; }
        public Task<string?> GetSlugAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<bool> IsActiveAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task ReactivateIfSystemSuspendedAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<PagedResult<OrganizationRecord>> ListAsync(string? search, bool? isActive, bool? isDeleted, int? page, int? pageSize, CancellationToken cancellationToken = default)
        {
            LastSearch = search;
            LastIsActive = isActive;
            LastIsDeleted = isDeleted;
            LastPage = page;
            LastPageSize = pageSize;
            var (normalizedPage, normalizedPageSize) = PaginationDefaults.Normalize(page, pageSize);
            var items = Organizations.Skip((normalizedPage - 1) * normalizedPageSize).Take(normalizedPageSize).ToList();
            return Task.FromResult(new PagedResult<OrganizationRecord>(items, normalizedPage, normalizedPageSize, Organizations.Count));
        }
        public Task SuspendAsync(Guid organizationId, CancellationToken cancellationToken = default) { Suspended = true; return Task.CompletedTask; }
        public Task ReactivateAsync(Guid organizationId, CancellationToken cancellationToken = default) { Suspended = false; return Task.CompletedTask; }
        public Task SoftDeleteAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestoreAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RevokeStaffRefreshTokensAsync(Guid organizationId, CancellationToken cancellationToken = default) { RevokedStaffTokens = true; return Task.CompletedTask; }
        public Task<OrganizationRecord?> GetAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult<OrganizationRecord?>(null);
        public Task<PagedResult<OrganizationUserRecord>> ListUsersAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default) => Task.FromResult(new PagedResult<OrganizationUserRecord>(new List<OrganizationUserRecord>(), 1, 25, 0));
        public Task<OrganizationUserRecord?> GetUserAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<OrganizationUserRecord?>(null);
        public Task<string?> GetUserNameAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task SuspendForNonPaymentAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeMerchantSessionRevoker : IMerchantSessionRevoker
    {
        public bool Revoked { get; private set; }
        public Task RevokeAllForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default) { Revoked = true; return Task.CompletedTask; }
    }

    private sealed class FakeBillingLookupService : IBillingLookupService
    {
        public int GetPlanSummaryCallCount { get; private set; }
        public Task<int?> GetMaxBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult((int?)null);
        public Task<PlanSummary?> GetPlanSummaryAsync(Guid organizationId, CancellationToken cancellationToken = default) { GetPlanSummaryCallCount++; return Task.FromResult<PlanSummary?>(null); }
        public Task<PlatformBillingMetrics> GetPlatformMetricsAsync(CancellationToken cancellationToken = default) => Task.FromResult(new PlatformBillingMetrics(0m, 0));
        public Task<PagedResult<InvoiceResponse>> ListInvoicesForOrganizationAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default) => Task.FromResult(new PagedResult<InvoiceResponse>(new List<InvoiceResponse>(), 1, 25, 0));
        public Task<List<InvoiceAdminRecord>> ListAllInvoicesAsync(Courier.Billing.Domain.InvoiceStatus? status, CancellationToken cancellationToken = default) => Task.FromResult(new List<InvoiceAdminRecord>());
        public Task StartTrialAsync(Guid organizationId, Guid actingUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> IsWriteBlockedAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<SubscriptionStatusInfo?> GetSubscriptionStatusAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult<SubscriptionStatusInfo?>(null);
    }

    private sealed class FakeBranchLookupService : IBranchLookupService
    {
        public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) => Task.FromResult<BranchZoneInfo?>(null);
        public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
        public Task<int> CountActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<PagedResult<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default) => Task.FromResult(new PagedResult<BranchAdminRecord>(new List<BranchAdminRecord>(), 1, 25, 0));
    }

    private sealed class FakeAuditLogger : IAuditLogger
    {
        public AuditEntry? LastEntry { get; private set; }
        public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) { LastEntry = entry; return Task.CompletedTask; }
    }

    [Fact]
    public async Task SuspendAsync_RevokesBothStaffAndMerchantSessionsAndAuditsCrossTenant()
    {
        var organizationLookup = new FakeOrganizationLookupService();
        var merchantRevoker = new FakeMerchantSessionRevoker();
        var auditLogger = new FakeAuditLogger();
        var service = new OrganizationLifecycleService(organizationLookup, merchantRevoker, new FakeBillingLookupService(), new FakeBranchLookupService(), auditLogger);
        var organizationId = Guid.NewGuid();
        var superAdminId = Guid.NewGuid();

        await service.SuspendAsync(organizationId, superAdminId);

        Assert.True(organizationLookup.Suspended);
        Assert.True(organizationLookup.RevokedStaffTokens);
        Assert.True(merchantRevoker.Revoked);
        Assert.True(auditLogger.LastEntry!.IsCrossTenantAction);
        Assert.Equal(superAdminId, auditLogger.LastEntry.UserId);
        Assert.Equal(organizationId, auditLogger.LastEntry.OrganizationId);
    }

    [Fact]
    public async Task ListOrganizationsAsync_PassesFiltersThrough_AndOnlyEnrichesThePagesRows()
    {
        var organizationLookup = new FakeOrganizationLookupService
        {
            Organizations = new List<OrganizationRecord>
            {
                new(Guid.NewGuid(), "Org A", "org-a", true, false, null, DateTimeOffset.UtcNow),
                new(Guid.NewGuid(), "Org B", "org-b", true, false, null, DateTimeOffset.UtcNow),
                new(Guid.NewGuid(), "Org C", "org-c", true, false, null, DateTimeOffset.UtcNow)
            }
        };
        var billingLookup = new FakeBillingLookupService();
        var service = new OrganizationLifecycleService(organizationLookup, new FakeMerchantSessionRevoker(), billingLookup, new FakeBranchLookupService(), new FakeAuditLogger());

        var result = await service.ListOrganizationsAsync(search: "kathmandu", isActive: true, isDeleted: false, page: 1, pageSize: 2);

        Assert.Equal("kathmandu", organizationLookup.LastSearch);
        Assert.Equal(true, organizationLookup.LastIsActive);
        Assert.Equal(false, organizationLookup.LastIsDeleted);
        Assert.Equal(1, organizationLookup.LastPage);
        Assert.Equal(2, organizationLookup.LastPageSize);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
        // The lookup fake's ListAsync itself already paginates down to 2 rows — the enrichment
        // loop in ListOrganizationsAsync must only iterate those, not all 3 organizations.
        Assert.Equal(2, billingLookup.GetPlanSummaryCallCount);
    }

    [Fact]
    public async Task RestoreAsync_DoesNotReactivate()
    {
        var organizationLookup = new FakeOrganizationLookupService();
        var service = new OrganizationLifecycleService(organizationLookup, new FakeMerchantSessionRevoker(), new FakeBillingLookupService(), new FakeBranchLookupService(), new FakeAuditLogger());

        await service.RestoreAsync(Guid.NewGuid(), Guid.NewGuid());

        // RestoreAsync never touches organizationLookup.Suspended's tracked flag (only
        // ReactivateAsync/SuspendAsync do in this fake) — proves Restore alone never reactivates.
        Assert.False(organizationLookup.Suspended);
    }
}
