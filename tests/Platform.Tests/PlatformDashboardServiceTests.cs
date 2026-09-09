using Courier.Audit.Application;
using Courier.Billing.Application;
using Courier.Branches.Application;
using Courier.Identity.Application;
using Courier.Platform.Application;
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
        public Task<List<OrganizationRecord>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult(Organizations);
        public Task SuspendAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReactivateAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SoftDeleteAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestoreAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RevokeStaffRefreshTokensAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<OrganizationRecord?> GetAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(Organizations.FirstOrDefault(o => o.Id == organizationId));
        public Task<List<OrganizationUserRecord>> ListUsersAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(new List<OrganizationUserRecord>());
        public Task<OrganizationUserRecord?> GetUserAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<OrganizationUserRecord?>(null);
        public Task SuspendForNonPaymentAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeBillingLookupService : IBillingLookupService
    {
        public PlatformBillingMetrics Metrics { get; set; } = new(0m, 0);
        public Task<int?> GetMaxBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult((int?)null);
        public Task<PlanSummary?> GetPlanSummaryAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult<PlanSummary?>(null);
        public Task<PlatformBillingMetrics> GetPlatformMetricsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Metrics);
        public Task<List<InvoiceResponse>> ListInvoicesForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(new List<InvoiceResponse>());
        public List<InvoiceAdminRecord> Invoices { get; set; } = new();
        public Task<List<InvoiceAdminRecord>> ListAllInvoicesAsync(Courier.Billing.Domain.InvoiceStatus? status, CancellationToken cancellationToken = default) => Task.FromResult(Invoices);
    }

    private sealed class FakeBranchLookupService : IBranchLookupService
    {
        public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) => Task.FromResult<BranchZoneInfo?>(null);
        public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
        public Task<int> CountActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<List<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(new List<BranchAdminRecord>());
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

        var invoices = await service.ListAllInvoicesAsync(null);

        Assert.Equal(2, invoices.Count);
        Assert.Equal("Kathmandu Couriers", invoices.Single(i => i.OrganizationId == knownOrgId).OrganizationName);
        Assert.Equal("Unknown", invoices.Single(i => i.OrganizationId == unknownOrgId).OrganizationName);
    }
}
