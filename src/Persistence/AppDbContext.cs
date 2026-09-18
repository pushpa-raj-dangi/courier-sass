using Courier.Audit.Domain;
using Courier.Audit.Infrastructure;
using Courier.Billing.Domain;
using Courier.Billing.Infrastructure;
using Courier.Branches.Domain;
using Courier.Branches.Infrastructure;
using Courier.Geography.Domain;
using Courier.Geography.Infrastructure;
using Courier.Identity.Domain;
using Courier.Identity.Infrastructure;
using Courier.Manifests.Domain;
using Courier.Manifests.Infrastructure;
using Courier.Merchants.Domain;
using Courier.Merchants.Infrastructure;
using Courier.Ndr.Domain;
using Courier.Ndr.Infrastructure;
using Courier.Cod.Domain;
using Courier.Cod.Infrastructure;
using Courier.Events.Domain;
using Courier.Events.Infrastructure;
using Courier.Notifications.Domain;
using Courier.Notifications.Infrastructure;
using Courier.Pricing.Domain;
using Courier.Webhooks.Domain;
using Courier.Webhooks.Infrastructure;
using Courier.Pricing.Infrastructure;
using Courier.Platform.Domain;
using Courier.Platform.Infrastructure;
using Courier.Riders.Domain;
using Courier.Riders.Infrastructure;
using Courier.Shipments.Domain;
using Courier.Shipments.Infrastructure;
using Courier.SharedKernel;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Courier.Persistence;

public class AppDbContext : DbContext, IDataProtectionKeyContext
{
    private readonly ICurrentTenantProvider _currentTenantProvider;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenantProvider currentTenantProvider)
        : base(options)
    {
        _currentTenantProvider = currentTenantProvider;
    }

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<UserBranch> UserBranches => Set<UserBranch>();
    public DbSet<UserInvite> UserInvites => Set<UserInvite>();
    public DbSet<Province> Provinces => Set<Province>();
    public DbSet<District> Districts => Set<District>();
    public DbSet<LocalLevel> LocalLevels => Set<LocalLevel>();
    public DbSet<Ward> Wards => Set<Ward>();
    public DbSet<Area> Areas => Set<Area>();
    public DbSet<DeliveryZone> DeliveryZones => Set<DeliveryZone>();
    public DbSet<Merchant> Merchants => Set<Merchant>();
    public DbSet<MerchantUser> MerchantUsers => Set<MerchantUser>();
    public DbSet<MerchantAddress> MerchantAddresses => Set<MerchantAddress>();
    public DbSet<MerchantKycDocument> MerchantKycDocuments => Set<MerchantKycDocument>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<MerchantRefreshToken> MerchantRefreshTokens => Set<MerchantRefreshToken>();
    public DbSet<ServiceType> ServiceTypes => Set<ServiceType>();
    public DbSet<WeightSlab> WeightSlabs => Set<WeightSlab>();
    public DbSet<RateCard> RateCards => Set<RateCard>();
    public DbSet<ZoneRate> ZoneRates => Set<ZoneRate>();
    public DbSet<AdditionalCharge> AdditionalCharges => Set<AdditionalCharge>();
    public DbSet<MerchantRateCard> MerchantRateCards => Set<MerchantRateCard>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<ShipmentItem> ShipmentItems => Set<ShipmentItem>();
    public DbSet<ShipmentEvent> ShipmentEvents => Set<ShipmentEvent>();
    public DbSet<PickupRequest> PickupRequests => Set<PickupRequest>();
    public DbSet<Manifest> Manifests => Set<Manifest>();
    public DbSet<ManifestShipment> ManifestShipments => Set<ManifestShipment>();
    public DbSet<ShipmentTransfer> ShipmentTransfers => Set<ShipmentTransfer>();
    public DbSet<Rider> Riders => Set<Rider>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<DeliveryRun> DeliveryRuns => Set<DeliveryRun>();
    public DbSet<DeliveryTask> DeliveryTasks => Set<DeliveryTask>();
    public DbSet<DeliveryAttempt> DeliveryAttempts => Set<DeliveryAttempt>();
    public DbSet<DeliveryProof> DeliveryProofs => Set<DeliveryProof>();
    public DbSet<RiderLocationPing> RiderLocationPings => Set<RiderLocationPing>();
    public DbSet<NDR> Ndrs => Set<NDR>();
    public DbSet<ReturnShipment> ReturnShipments => Set<ReturnShipment>();
    public DbSet<CODCollection> CODCollections => Set<CODCollection>();
    public DbSet<RiderCashSettlement> RiderCashSettlements => Set<RiderCashSettlement>();
    public DbSet<BranchCashSettlement> BranchCashSettlements => Set<BranchCashSettlement>();
    public DbSet<FinancialTransaction> FinancialTransactions => Set<FinancialTransaction>();
    public DbSet<MerchantSettlement> MerchantSettlements => Set<MerchantSettlement>();
    public DbSet<MerchantSettlementItem> MerchantSettlementItems => Set<MerchantSettlementItem>();
    public DbSet<Adjustment> Adjustments => Set<Adjustment>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();
    public DbSet<WebhookSubscription> WebhookSubscriptions => Set<WebhookSubscription>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PlatformSettingDefinition> PlatformSettingDefinitions => Set<PlatformSettingDefinition>();
    public DbSet<PlatformSetting> PlatformSettings => Set<PlatformSetting>();

    // IDataProtectionKeyContext requires a settable property; the setter is never exercised at
    // runtime (PersistKeysToDbContext only ever reads via Set<DataProtectionKey>()) but is kept
    // to satisfy the interface contract without relying on undocumented reflection behavior.
    public DbSet<DataProtectionKey> DataProtectionKeys
    {
        get => Set<DataProtectionKey>();
        set { }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OrganizationConfiguration());
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new RoleConfiguration());
        modelBuilder.ApplyConfiguration(new PermissionConfiguration());
        modelBuilder.ApplyConfiguration(new UserRoleConfiguration());
        modelBuilder.ApplyConfiguration(new RefreshTokenConfiguration());
        modelBuilder.ApplyConfiguration(new AuditLogConfiguration());
        modelBuilder.ApplyConfiguration(new BranchConfiguration());
        modelBuilder.ApplyConfiguration(new UserBranchConfiguration());
        // No tenant filter on UserInvite: no own OrganizationId — tenant isolation flows
        // transitively through UserId -> User.OrganizationId (which does have a filter, above).
        modelBuilder.ApplyConfiguration(new UserInviteConfiguration());
        modelBuilder.ApplyConfiguration(new OrganizationSettingsConfiguration());
        modelBuilder.ApplyConfiguration(new ProvinceConfiguration());
        modelBuilder.ApplyConfiguration(new DistrictConfiguration());
        modelBuilder.ApplyConfiguration(new LocalLevelConfiguration());
        modelBuilder.ApplyConfiguration(new WardConfiguration());
        modelBuilder.ApplyConfiguration(new AreaConfiguration());
        modelBuilder.ApplyConfiguration(new DeliveryZoneConfiguration());
        modelBuilder.ApplyConfiguration(new MerchantConfiguration());
        modelBuilder.ApplyConfiguration(new MerchantUserConfiguration());
        modelBuilder.ApplyConfiguration(new MerchantAddressConfiguration());
        modelBuilder.ApplyConfiguration(new MerchantKycDocumentConfiguration());
        modelBuilder.ApplyConfiguration(new ApiKeyConfiguration());
        modelBuilder.ApplyConfiguration(new MerchantRefreshTokenConfiguration());
        modelBuilder.ApplyConfiguration(new ServiceTypeConfiguration());
        modelBuilder.ApplyConfiguration(new WeightSlabConfiguration());
        modelBuilder.ApplyConfiguration(new RateCardConfiguration());
        modelBuilder.ApplyConfiguration(new ZoneRateConfiguration());
        modelBuilder.ApplyConfiguration(new AdditionalChargeConfiguration());
        modelBuilder.ApplyConfiguration(new MerchantRateCardConfiguration());
        modelBuilder.ApplyConfiguration(new ShipmentConfiguration());
        modelBuilder.ApplyConfiguration(new ShipmentItemConfiguration());
        modelBuilder.ApplyConfiguration(new ShipmentEventConfiguration());
        modelBuilder.ApplyConfiguration(new PickupRequestConfiguration());
        modelBuilder.ApplyConfiguration(new ManifestConfiguration());
        modelBuilder.ApplyConfiguration(new ManifestShipmentConfiguration());
        modelBuilder.ApplyConfiguration(new ShipmentTransferConfiguration());
        modelBuilder.ApplyConfiguration(new RiderConfiguration());
        modelBuilder.ApplyConfiguration(new VehicleConfiguration());
        modelBuilder.ApplyConfiguration(new DeliveryRunConfiguration());
        modelBuilder.ApplyConfiguration(new DeliveryTaskConfiguration());
        modelBuilder.ApplyConfiguration(new DeliveryAttemptConfiguration());
        modelBuilder.ApplyConfiguration(new DeliveryProofConfiguration());
        modelBuilder.ApplyConfiguration(new RiderLocationPingConfiguration());
        modelBuilder.ApplyConfiguration(new NdrConfiguration());
        modelBuilder.ApplyConfiguration(new ReturnShipmentConfiguration());
        modelBuilder.ApplyConfiguration(new CODCollectionConfiguration());
        modelBuilder.ApplyConfiguration(new RiderCashSettlementConfiguration());
        modelBuilder.ApplyConfiguration(new BranchCashSettlementConfiguration());
        modelBuilder.ApplyConfiguration(new FinancialTransactionConfiguration());
        modelBuilder.ApplyConfiguration(new MerchantSettlementConfiguration());
        modelBuilder.ApplyConfiguration(new MerchantSettlementItemConfiguration());
        modelBuilder.ApplyConfiguration(new AdjustmentConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new NotificationTemplateConfiguration());
        modelBuilder.ApplyConfiguration(new NotificationLogConfiguration());
        modelBuilder.ApplyConfiguration(new WebhookSubscriptionConfiguration());
        modelBuilder.ApplyConfiguration(new WebhookDeliveryConfiguration());
        modelBuilder.ApplyConfiguration(new PlanConfiguration());
        modelBuilder.ApplyConfiguration(new Courier.Billing.Infrastructure.SubscriptionConfiguration());
        modelBuilder.ApplyConfiguration(new Courier.Billing.Infrastructure.InvoiceConfiguration());
        modelBuilder.ApplyConfiguration(new Courier.Billing.Infrastructure.PaymentConfiguration());
        modelBuilder.ApplyConfiguration(new PlatformSettingDefinitionConfiguration());
        modelBuilder.ApplyConfiguration(new PlatformSettingConfiguration());

        // Branch.OrganizationId -> Organizations FK lives here rather than in
        // BranchConfiguration (Branches module) because Branches deliberately has no project
        // reference to Identity — the dependency runs the other way (Identity -> Branches, for
        // UserBranch's FK to Branch and IBranchHierarchyService). Persistence already
        // references both modules, so it's the one place that can wire this specific
        // constraint without introducing a project cycle.
        modelBuilder.Entity<Branch>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(b => b.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Same reasoning for DeliveryZone.OrganizationId: Geography has no project reference to
        // Identity.
        modelBuilder.Entity<DeliveryZone>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(z => z.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Same reasoning for all four Merchants entities' OrganizationId: Merchants has no
        // project reference to Identity either.
        modelBuilder.Entity<Merchant>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(m => m.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MerchantUser>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(mu => mu.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MerchantAddress>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(a => a.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MerchantKycDocument>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(d => d.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ApiKey>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(k => k.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Same reasoning for RateCard/MerchantRateCard.OrganizationId: Pricing has no project
        // reference to Identity either.
        modelBuilder.Entity<RateCard>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(rc => rc.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MerchantRateCard>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(mrc => mrc.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Same reasoning for Shipment.OrganizationId: Shipments has no project reference to
        // Identity either.
        modelBuilder.Entity<Shipment>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(s => s.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Same reasoning for PickupRequest.OrganizationId and PickupRequest.BranchId: Shipments
        // has no project reference to Identity either.
        modelBuilder.Entity<PickupRequest>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(p => p.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<PickupRequest>().HasOne<Branch>()
            .WithMany()
            .HasForeignKey(p => p.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        // Same reasoning for Manifest.OrganizationId: Manifests has no project reference to
        // Identity either.
        modelBuilder.Entity<Manifest>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(m => m.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Same reasoning for Rider/Vehicle/DeliveryAttempt/DeliveryProof.OrganizationId: Riders has
        // no project reference to Identity's Infrastructure/Persistence layer (only Identity's
        // Application-layer IUserProvisioningService interface).
        modelBuilder.Entity<Rider>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(r => r.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Vehicle>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(v => v.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<DeliveryAttempt>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(a => a.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<DeliveryProof>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(p => p.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Same reasoning for Ndr/ReturnShipment.OrganizationId: Ndr has no project reference to
        // Identity.
        modelBuilder.Entity<NDR>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(n => n.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ReturnShipment>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(r => r.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Same reasoning for the six Cod entities below that carry their own OrganizationId: Cod
        // has no project reference to Identity. MerchantSettlementItem is excluded (no own
        // OrganizationId — see MerchantSettlementItemConfiguration).
        modelBuilder.Entity<CODCollection>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(c => c.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<RiderCashSettlement>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(r => r.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BranchCashSettlement>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(b => b.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<FinancialTransaction>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(f => f.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MerchantSettlement>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(m => m.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Adjustment>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(a => a.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Same reasoning as every other OrganizationId-carrying entity above: Events has no
        // project reference to Identity.
        modelBuilder.Entity<OutboxMessage>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(m => m.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<NotificationTemplate>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(t => t.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<NotificationLog>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(l => l.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<WebhookSubscription>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(s => s.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        // No FK to Merchant — Webhooks' project reference to Merchants is Application-layer only
        // (validating a caller-supplied MerchantId exists, Task 10), not its EF model, same
        // cross-module precedent as Shipment.MerchantId (Phase 6).

        // Tenant filters fail CLOSED: the explicit `OrganizationId != null` guard stops EF Core's
        // null-semantics translation from degrading these into `WHERE "OrganizationId" IS NULL`
        // when no tenant is resolved, which would otherwise match org-less rows (planned for
        // future platform/SuperAdmin accounts) instead of matching nothing.
        modelBuilder.Entity<User>().HasQueryFilter(u => _currentTenantProvider.OrganizationId != null && u.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<Role>().HasQueryFilter(r => _currentTenantProvider.OrganizationId != null && r.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<UserRole>().HasQueryFilter(ur => _currentTenantProvider.OrganizationId != null && ur.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<AuditLog>().HasQueryFilter(a => _currentTenantProvider.OrganizationId != null && a.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<Branch>().HasQueryFilter(b => _currentTenantProvider.OrganizationId != null && b.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<UserBranch>().HasQueryFilter(ub => _currentTenantProvider.OrganizationId != null && ub.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<OrganizationSettings>().HasQueryFilter(s => _currentTenantProvider.OrganizationId != null && s.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<DeliveryZone>().HasQueryFilter(z => _currentTenantProvider.OrganizationId != null && z.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<Merchant>().HasQueryFilter(m => _currentTenantProvider.OrganizationId != null && m.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<MerchantUser>().HasQueryFilter(mu => _currentTenantProvider.OrganizationId != null && mu.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<MerchantAddress>().HasQueryFilter(a => _currentTenantProvider.OrganizationId != null && a.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<MerchantKycDocument>().HasQueryFilter(d => _currentTenantProvider.OrganizationId != null && d.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<ApiKey>().HasQueryFilter(k => _currentTenantProvider.OrganizationId != null && k.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<RateCard>().HasQueryFilter(rc => _currentTenantProvider.OrganizationId != null && rc.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<MerchantRateCard>().HasQueryFilter(mrc => _currentTenantProvider.OrganizationId != null && mrc.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<Shipment>().HasQueryFilter(s => _currentTenantProvider.OrganizationId != null && s.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<PickupRequest>().HasQueryFilter(p => _currentTenantProvider.OrganizationId != null && p.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<Manifest>().HasQueryFilter(m => _currentTenantProvider.OrganizationId != null && m.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<Rider>().HasQueryFilter(r => _currentTenantProvider.OrganizationId != null && r.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<Vehicle>().HasQueryFilter(v => _currentTenantProvider.OrganizationId != null && v.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<DeliveryAttempt>().HasQueryFilter(a => _currentTenantProvider.OrganizationId != null && a.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<DeliveryProof>().HasQueryFilter(p => _currentTenantProvider.OrganizationId != null && p.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<NDR>().HasQueryFilter(n => _currentTenantProvider.OrganizationId != null && n.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<ReturnShipment>().HasQueryFilter(r => _currentTenantProvider.OrganizationId != null && r.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<CODCollection>().HasQueryFilter(c => _currentTenantProvider.OrganizationId != null && c.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<RiderCashSettlement>().HasQueryFilter(r => _currentTenantProvider.OrganizationId != null && r.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<BranchCashSettlement>().HasQueryFilter(b => _currentTenantProvider.OrganizationId != null && b.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<FinancialTransaction>().HasQueryFilter(f => _currentTenantProvider.OrganizationId != null && f.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<MerchantSettlement>().HasQueryFilter(m => _currentTenantProvider.OrganizationId != null && m.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<Adjustment>().HasQueryFilter(a => _currentTenantProvider.OrganizationId != null && a.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<OutboxMessage>().HasQueryFilter(m => _currentTenantProvider.OrganizationId != null && m.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<NotificationTemplate>().HasQueryFilter(t => _currentTenantProvider.OrganizationId != null && t.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<NotificationLog>().HasQueryFilter(l => _currentTenantProvider.OrganizationId != null && l.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<WebhookSubscription>().HasQueryFilter(s => _currentTenantProvider.OrganizationId != null && s.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<Courier.Billing.Domain.Subscription>().HasQueryFilter(s => _currentTenantProvider.OrganizationId != null && s.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<Courier.Billing.Domain.Invoice>().HasQueryFilter(i => _currentTenantProvider.OrganizationId != null && i.OrganizationId == _currentTenantProvider.OrganizationId);
        // No tenant filter on Payment: no own OrganizationId — tenant isolation flows
        // transitively through InvoiceId -> Invoice.OrganizationId (which does have a filter,
        // above), same pattern as ShipmentItem/ManifestShipment.

        // No tenant filter on WebhookDelivery: no own OrganizationId — tenant isolation flows
        // transitively through WebhookSubscriptionId -> WebhookSubscription.OrganizationId
        // (which does have a filter, above), same pattern as MerchantSettlementItem.

        // No tenant filter on MerchantSettlementItem: no own OrganizationId — tenant isolation
        // flows transitively through MerchantSettlementId -> MerchantSettlement.OrganizationId
        // (which does have a filter, above). Every query against this table in
        // Cod.Application.CodService scopes through a MerchantSettlement the caller's tenant
        // already owns.

        // Defensive: nothing queries Organizations with a tenant context today (registration only
        // inserts), but without this a future `Organizations.ToListAsync()` would enumerate every
        // tenant on the platform.
        modelBuilder.Entity<Organization>().HasQueryFilter(o => _currentTenantProvider.OrganizationId != null && o.Id == _currentTenantProvider.OrganizationId);

        // No tenant filter on RefreshToken/MerchantRefreshToken: their lookups happen before the
        // caller's tenant is known (see AuthService.RefreshTokenAsync / MerchantAuthService.
        // RefreshTokenAsync), and rows are keyed by an unguessable SHA-256 token hash rather than
        // being enumerable. Filtering it would break the refresh flow outright.

        // No tenant filter on Province/District/LocalLevel/Ward/Area: shared reference data,
        // identical across every tenant, per ARCHITECTURE.md §2's Geography module description.

        // No tenant filter on ServiceType/WeightSlab: shared reference data, identical across
        // every tenant (same reasoning as Province/District/LocalLevel/Ward/Area above).

        // No tenant filter on ZoneRate/AdditionalCharge: neither carries its own OrganizationId —
        // tenant isolation flows transitively through RateCardId -> RateCard.OrganizationId
        // (which does have a filter, above). Every query against these two tables in
        // Pricing.Application scopes through a RateCard the caller's tenant already owns.

        // No tenant filter on ShipmentItem/ShipmentEvent: neither carries its own
        // OrganizationId — tenant isolation flows transitively through
        // ShipmentId -> Shipment.OrganizationId (which does have a filter, above). Every query
        // against these two tables in Shipments.Application scopes through a Shipment the
        // caller's tenant already owns.

        // No tenant filter on ManifestShipment/ShipmentTransfer: neither carries its own
        // OrganizationId — tenant isolation flows transitively through
        // ManifestId -> Manifest.OrganizationId (which does have a filter, above).

        // No tenant filter on DeliveryRun/DeliveryTask: neither carries its own OrganizationId —
        // tenant isolation flows transitively through RiderId -> Rider.OrganizationId (which does
        // have a filter, above). Every query against these two tables in Riders.Application scopes
        // through a Rider the caller's tenant already owns.
    }
}
