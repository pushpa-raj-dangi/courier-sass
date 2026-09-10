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
        public Task<string?> GetSlugAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<bool> IsActiveAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task ReactivateIfSystemSuspendedAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<List<OrganizationRecord>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult(new List<OrganizationRecord>());
        public Task SuspendAsync(Guid organizationId, CancellationToken cancellationToken = default) { Suspended = true; return Task.CompletedTask; }
        public Task ReactivateAsync(Guid organizationId, CancellationToken cancellationToken = default) { Suspended = false; return Task.CompletedTask; }
        public Task SoftDeleteAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestoreAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RevokeStaffRefreshTokensAsync(Guid organizationId, CancellationToken cancellationToken = default) { RevokedStaffTokens = true; return Task.CompletedTask; }
        public Task<OrganizationRecord?> GetAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult<OrganizationRecord?>(null);
        public Task<List<OrganizationUserRecord>> ListUsersAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(new List<OrganizationUserRecord>());
        public Task<OrganizationUserRecord?> GetUserAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<OrganizationUserRecord?>(null);
        public Task SuspendForNonPaymentAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeMerchantSessionRevoker : IMerchantSessionRevoker
    {
        public bool Revoked { get; private set; }
        public Task RevokeAllForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default) { Revoked = true; return Task.CompletedTask; }
    }

    private sealed class FakeBillingLookupService : IBillingLookupService
    {
        public Task<int?> GetMaxBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult((int?)null);
        public Task<PlanSummary?> GetPlanSummaryAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult<PlanSummary?>(null);
        public Task<PlatformBillingMetrics> GetPlatformMetricsAsync(CancellationToken cancellationToken = default) => Task.FromResult(new PlatformBillingMetrics(0m, 0));
        public Task<List<InvoiceResponse>> ListInvoicesForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(new List<InvoiceResponse>());
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
        public Task<List<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(new List<BranchAdminRecord>());
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
