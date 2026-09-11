using Courier.Audit.Application;
using Courier.Billing.Application;
using Courier.Branches.Application;
using Courier.Identity.Application;
using Courier.Platform.Application;
using Courier.SharedKernel;
using Xunit;

namespace Courier.Platform.Tests;

public class PlatformDashboardServiceTests
{
    private sealed class FakeOrganizationLookupService : IOrganizationLookupService
    {
        public List<OrganizationRecord> Organizations { get; set; } = new();
        public Task<string?> GetSlugAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<bool> IsActiveAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task ReactivateIfSystemSuspendedAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        // Mirrors OrganizationAdminService.ListAsync's real filter/pagination semantics closely
        // enough for GetMetricsAsync's 4 filtered-count calls (isActive/isDeleted combos) and
        // ListAllInvoicesAsync's pagination loop to behave correctly against this fake.
        public Task<PagedResult<OrganizationRecord>> ListAsync(string? search, bool? isActive, bool? isDeleted, int? page, int? pageSize, CancellationToken cancellationToken = default)
        {
            var query = Organizations.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.ToLower();
                query = query.Where(o => o.Name.ToLower().Contains(term) || o.Slug.ToLower().Contains(term));
            }
            if (isActive is bool activeFilter)
            {
                query = query.Where(o => o.IsActive == activeFilter);
            }
            if (isDeleted is bool deletedFilter)
            {
                query = deletedFilter ? query.Where(o => o.DeletedAt != null) : query.Where(o => o.DeletedAt == null);
            }
            var filtered = query.OrderBy(o => o.Name).ToList();
            var (normalizedPage, normalizedPageSize) = PaginationDefaults.Normalize(page, pageSize);
            var items = filtered.Skip((normalizedPage - 1) * normalizedPageSize).Take(normalizedPageSize).ToList();
            return Task.FromResult(new PagedResult<OrganizationRecord>(items, normalizedPage, normalizedPageSize, filtered.Count));
        }
        public Task SuspendAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReactivateAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SoftDeleteAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestoreAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RevokeStaffRefreshTokensAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<OrganizationRecord?> GetAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(Organizations.FirstOrDefault(o => o.Id == organizationId));
        public Task<PagedResult<OrganizationUserRecord>> ListUsersAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default) => Task.FromResult(new PagedResult<OrganizationUserRecord>(new List<OrganizationUserRecord>(), 1, 25, 0));
        public Task<OrganizationUserRecord?> GetUserAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<OrganizationUserRecord?>(null);
        public Task SuspendForNonPaymentAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeBillingLookupService : IBillingLookupService
    {
        public PlatformBillingMetrics Metrics { get; set; } = new(0m, 0);
        public Task<int?> GetMaxBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult((int?)null);
        public Task<PlanSummary?> GetPlanSummaryAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult<PlanSummary?>(null);
        public Task<PlatformBillingMetrics> GetPlatformMetricsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Metrics);
        public Task<PagedResult<InvoiceResponse>> ListInvoicesForOrganizationAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default) => Task.FromResult(new PagedResult<InvoiceResponse>(new List<InvoiceResponse>(), 1, 25, 0));
        public List<InvoiceAdminRecord> Invoices { get; set; } = new();
        public Task<List<InvoiceAdminRecord>> ListAllInvoicesAsync(Courier.Billing.Domain.InvoiceStatus? status, CancellationToken cancellationToken = default) => Task.FromResult(Invoices);
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

    private sealed class FakeAuditLogReader : IAuditLogReader
    {
        public Task<List<AuditLogRecord>> ListForOrganizationAsync(Guid organizationId, int limit, CancellationToken cancellationToken = default) => Task.FromResult(new List<AuditLogRecord>());
    }

    [Fact]
    public async Task GetMetricsAsync_CountsOrganizationsByStatus()
    {
        var organizationLookup = new FakeOrganizationLookupService
        {
            Organizations = new List<OrganizationRecord>
            {
                new(Guid.NewGuid(), "Active Org", "active-org", true, false, null, DateTimeOffset.UtcNow),
                new(Guid.NewGuid(), "Suspended Org", "suspended-org", false, false, null, DateTimeOffset.UtcNow),
                new(Guid.NewGuid(), "Deleted Org", "deleted-org", false, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
            }
        };
        var billingLookup = new FakeBillingLookupService { Metrics = new PlatformBillingMetrics(5000m, 2) };
        var service = new PlatformDashboardService(organizationLookup, billingLookup, new FakeBranchLookupService(), new FakeAuditLogReader());

        var metrics = await service.GetMetricsAsync();

        Assert.Equal(3, metrics.TotalOrganizations);
        Assert.Equal(1, metrics.ActiveOrganizations);
        Assert.Equal(1, metrics.SuspendedOrganizations);
        Assert.Equal(1, metrics.DeletedOrganizations);
        Assert.Equal(5000m, metrics.MonthlyRecurringRevenueNpr);
        Assert.Equal(2, metrics.OverdueInvoiceCount);
    }

    [Fact]
    public async Task GetOrganizationDetailAsync_UnknownOrganization_ReturnsNull()
    {
        var service = new PlatformDashboardService(new FakeOrganizationLookupService(), new FakeBillingLookupService(), new FakeBranchLookupService(), new FakeAuditLogReader());

        var detail = await service.GetOrganizationDetailAsync(Guid.NewGuid());

        Assert.Null(detail);
    }

    [Fact]
    public async Task ListAllInvoicesAsync_JoinsOrganizationNameInMemory_AndFallsBackToUnknown()
    {
        var knownOrgId = Guid.NewGuid();
        var unknownOrgId = Guid.NewGuid();
        var organizationLookup = new FakeOrganizationLookupService
        {
            Organizations = new List<OrganizationRecord>
            {
                new(knownOrgId, "Kathmandu Couriers", "kathmandu-couriers", true, false, null, DateTimeOffset.UtcNow)
            }
        };
        var billingLookup = new FakeBillingLookupService
        {
            Invoices = new List<InvoiceAdminRecord>
            {
                new(Guid.NewGuid(), knownOrgId, "Growth", 2500m, Courier.Billing.Domain.InvoiceStatus.Paid, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
                new(Guid.NewGuid(), unknownOrgId, "Starter", 1000m, Courier.Billing.Domain.InvoiceStatus.Overdue, DateTimeOffset.UtcNow, null)
            }
        };
        var service = new PlatformDashboardService(organizationLookup, billingLookup, new FakeBranchLookupService(), new FakeAuditLogReader());

        var result = await service.ListAllInvoicesAsync(null, null, null, null);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal("Kathmandu Couriers", result.Items.Single(i => i.OrganizationId == knownOrgId).OrganizationName);
        Assert.Equal("Unknown", result.Items.Single(i => i.OrganizationId == unknownOrgId).OrganizationName);
    }

    [Fact]
    public async Task ListAllInvoicesAsync_Search_FiltersToMatchingOrganizationOnly()
    {
        var kathmanduOrgId = Guid.NewGuid();
        var pokharaOrgId = Guid.NewGuid();
        var organizationLookup = new FakeOrganizationLookupService
        {
            Organizations = new List<OrganizationRecord>
            {
                new(kathmanduOrgId, "Kathmandu Couriers", "kathmandu-couriers", true, false, null, DateTimeOffset.UtcNow),
                new(pokharaOrgId, "Pokhara Express", "pokhara-express", true, false, null, DateTimeOffset.UtcNow)
            }
        };
        var billingLookup = new FakeBillingLookupService
        {
            Invoices = new List<InvoiceAdminRecord>
            {
                new(Guid.NewGuid(), kathmanduOrgId, "Growth", 2500m, Courier.Billing.Domain.InvoiceStatus.Paid, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
                new(Guid.NewGuid(), pokharaOrgId, "Starter", 1000m, Courier.Billing.Domain.InvoiceStatus.Paid, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
            }
        };
        var service = new PlatformDashboardService(organizationLookup, billingLookup, new FakeBranchLookupService(), new FakeAuditLogReader());

        var result = await service.ListAllInvoicesAsync(null, "Kathmandu", null, null);

        var invoice = Assert.Single(result.Items);
        Assert.Equal(kathmanduOrgId, invoice.OrganizationId);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task ListAllInvoicesAsync_Pagination_ReturnsCorrectSliceAndTotalCount()
    {
        var orgId = Guid.NewGuid();
        var organizationLookup = new FakeOrganizationLookupService
        {
            Organizations = new List<OrganizationRecord> { new(orgId, "Kathmandu Couriers", "kathmandu-couriers", true, false, null, DateTimeOffset.UtcNow) }
        };
        var billingLookup = new FakeBillingLookupService
        {
            Invoices = Enumerable.Range(0, 5)
                .Select(i => new InvoiceAdminRecord(Guid.NewGuid(), orgId, "Growth", 2500m, Courier.Billing.Domain.InvoiceStatus.Paid, DateTimeOffset.UtcNow.AddDays(i), DateTimeOffset.UtcNow))
                .ToList()
        };
        var service = new PlatformDashboardService(organizationLookup, billingLookup, new FakeBranchLookupService(), new FakeAuditLogReader());

        var result = await service.ListAllInvoicesAsync(null, null, page: 2, pageSize: 2);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(2, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(3, result.TotalPages);
    }

    [Fact]
    public async Task ListAllInvoicesAsync_StatusFilter_StillAppliedByBillingLookup()
    {
        var orgId = Guid.NewGuid();
        var organizationLookup = new FakeOrganizationLookupService
        {
            Organizations = new List<OrganizationRecord> { new(orgId, "Kathmandu Couriers", "kathmandu-couriers", true, false, null, DateTimeOffset.UtcNow) }
        };
        // The fake's ListAllInvoicesAsync ignores `status` and returns everything (matching the
        // real IBillingLookupService's ListAllInvoicesAsync contract, which this fake only
        // partially stubs) — this test just confirms the status param reaches through PlatformDashboardService
        // to IBillingLookupService.ListAllInvoicesAsync unchanged; SubscriptionServiceTests already covers
        // the real filtering behavior against a real DB.
        var recordingBillingLookup = new RecordingStatusBillingLookupService();
        var service = new PlatformDashboardService(organizationLookup, recordingBillingLookup, new FakeBranchLookupService(), new FakeAuditLogReader());

        await service.ListAllInvoicesAsync(Courier.Billing.Domain.InvoiceStatus.Overdue, null, null, null);

        Assert.Equal(Courier.Billing.Domain.InvoiceStatus.Overdue, recordingBillingLookup.LastStatus);
    }

    private sealed class RecordingStatusBillingLookupService : IBillingLookupService
    {
        public Courier.Billing.Domain.InvoiceStatus? LastStatus { get; private set; }
        public Task<int?> GetMaxBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult((int?)null);
        public Task<PlanSummary?> GetPlanSummaryAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult<PlanSummary?>(null);
        public Task<PlatformBillingMetrics> GetPlatformMetricsAsync(CancellationToken cancellationToken = default) => Task.FromResult(new PlatformBillingMetrics(0m, 0));
        public Task<PagedResult<InvoiceResponse>> ListInvoicesForOrganizationAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default) => Task.FromResult(new PagedResult<InvoiceResponse>(new List<InvoiceResponse>(), 1, 25, 0));
        public Task<List<InvoiceAdminRecord>> ListAllInvoicesAsync(Courier.Billing.Domain.InvoiceStatus? status, CancellationToken cancellationToken = default)
        {
            LastStatus = status;
            return Task.FromResult(new List<InvoiceAdminRecord>());
        }
        public Task StartTrialAsync(Guid organizationId, Guid actingUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> IsWriteBlockedAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<SubscriptionStatusInfo?> GetSubscriptionStatusAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult<SubscriptionStatusInfo?>(null);
    }
}
