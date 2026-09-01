using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Cod.Application;
using Courier.Events.Application;
using Courier.Identity.Application;
using Courier.Ndr.Application;
using Courier.Riders.Domain;
using Courier.Shipments.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Riders.Application;

public partial class RiderService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly IUserProvisioningService _userProvisioningService;
    private readonly IBranchLookupService _branchLookupService;
    private readonly IBranchAuthorizationChecker _branchAuthorizationChecker;
    private readonly IShipmentTransitionService _shipmentTransitionService;
    private readonly IObjectStorage _objectStorage;
    private readonly INdrCreationService _ndrCreationService;
    private readonly ICodCollectionService _codCollectionService;
    private readonly IPermissionChecker _permissionChecker;
    private readonly IOutboxWriter _outboxWriter;
    private readonly IShipmentTaskLookupService _shipmentTaskLookupService;

    public RiderService(
        DbContext dbContext,
        ICurrentTenantProvider currentTenantProvider,
        IAuditLogger auditLogger,
        IUserProvisioningService userProvisioningService,
        IBranchLookupService branchLookupService,
        IBranchAuthorizationChecker branchAuthorizationChecker,
        IShipmentTransitionService shipmentTransitionService,
        IObjectStorage objectStorage,
        INdrCreationService ndrCreationService,
        ICodCollectionService codCollectionService,
        IPermissionChecker permissionChecker,
        IOutboxWriter outboxWriter,
        IShipmentTaskLookupService shipmentTaskLookupService)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _userProvisioningService = userProvisioningService;
        _branchLookupService = branchLookupService;
        _branchAuthorizationChecker = branchAuthorizationChecker;
        _shipmentTransitionService = shipmentTransitionService;
        _objectStorage = objectStorage;
        _ndrCreationService = ndrCreationService;
        _codCollectionService = codCollectionService;
        _permissionChecker = permissionChecker;
        _outboxWriter = outboxWriter;
        _shipmentTaskLookupService = shipmentTaskLookupService;
    }

    public async Task<RiderResponse> CreateRiderAsync(CreateRiderRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new RiderValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new RiderValidationException("Acting user could not be resolved.");

        if (await _branchLookupService.GetZoneInfoAsync(request.BaseBranchId, cancellationToken) is null)
        {
            throw new RiderValidationException("BaseBranchId does not refer to a branch in this organization.");
        }
        await CheckBranchAccessAsync(actingUser, "rider.create", request.BaseBranchId, cancellationToken);

        // Maps 1:1 onto the role names AuthService.RegisterOrganizationAsync seeds (Task 3).
        var roleName = request.RoleType == RiderRoleType.PickupRider ? "PickupRider" : "DeliveryRider";

        Guid userId;
        try
        {
            userId = await _userProvisioningService.ProvisionStaffUserAsync(
                new ProvisionStaffUserRequest(request.Email, request.Password, request.FullName, request.Phone, roleName, request.BaseBranchId),
                organizationId, actingUserId, cancellationToken);
        }
        catch (UserProvisioningException ex)
        {
            throw new RiderValidationException(ex.Message);
        }

        var rider = new Rider
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            UserId = userId,
            BaseBranchId = request.BaseBranchId,
            LicenseNumber = request.LicenseNumber,
            RoleType = request.RoleType,
            IsActive = true
        };
        _dbContext.Set<Rider>().Add(rider);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "rider.created", "Rider", rider.Id.ToString()), cancellationToken);
        return ToResponse(rider);
    }

    public async Task<VehicleResponse> CreateVehicleAsync(CreateVehicleRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new RiderValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new RiderValidationException("Acting user could not be resolved.");

        if (request.RiderId is Guid riderId)
        {
            var rider = await _dbContext.Set<Rider>().FirstOrDefaultAsync(r => r.Id == riderId, cancellationToken)
                ?? throw new RiderValidationException("RiderId does not refer to a rider in this organization.");
            await CheckBranchAccessAsync(actingUser, "rider.create", rider.BaseBranchId, cancellationToken);
        }

        var vehicle = new Vehicle
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            RiderId = request.RiderId,
            Type = request.Type,
            PlateNumber = request.PlateNumber,
            Capacity = request.Capacity
        };
        _dbContext.Set<Vehicle>().Add(vehicle);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "vehicle.created", "Vehicle", vehicle.Id.ToString()), cancellationToken);
        return ToVehicleResponse(vehicle);
    }

    private async Task CheckBranchAccessAsync(ClaimsPrincipal actingUser, string permission, Guid branchId, CancellationToken cancellationToken)
    {
        var accessible = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, permission, cancellationToken);
        if (accessible is not null && !accessible.Contains(branchId))
        {
            throw new RiderValidationException($"Branch is outside your branch access for permission '{permission}'.");
        }
    }

    private static RiderResponse ToResponse(Rider rider) => new(
        rider.Id, rider.UserId, rider.BaseBranchId, rider.LicenseNumber, rider.RoleType, rider.IsActive);

    private static VehicleResponse ToVehicleResponse(Vehicle vehicle) => new(
        vehicle.Id, vehicle.RiderId, vehicle.Type, vehicle.PlateNumber, vehicle.Capacity);
}
