using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Geography.Application;
using Courier.Merchants.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Merchants.Application;

public class MerchantOnboardingService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly IMerchantPasswordHasher _passwordHasher;
    private readonly IGeographyLookupService _geographyLookupService;

    public MerchantOnboardingService(
        DbContext dbContext,
        ICurrentTenantProvider currentTenantProvider,
        IAuditLogger auditLogger,
        IMerchantPasswordHasher passwordHasher,
        IGeographyLookupService geographyLookupService)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _passwordHasher = passwordHasher;
        _geographyLookupService = geographyLookupService;
    }

    public async Task<MerchantResponse> CreateMerchantAsync(CreateMerchantRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new MerchantValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new MerchantValidationException("Acting user could not be resolved.");

        var ownerEmail = request.OwnerEmail.ToLowerInvariant();
        var emailTaken = await _dbContext.Set<MerchantUser>().IgnoreQueryFilters()
            .AnyAsync(mu => mu.Email == ownerEmail, cancellationToken);
        if (emailTaken)
        {
            throw new MerchantValidationException($"Email '{ownerEmail}' is already registered to a merchant account.");
        }

        var validGeography = await _geographyLookupService.IsValidHierarchyAsync(
            request.ProvinceId, request.DistrictId, request.LocalLevelId, request.WardNumber, cancellationToken);
        if (!validGeography)
        {
            throw new MerchantValidationException("Province/District/LocalLevel/WardNumber do not form a valid geography chain.");
        }

        var merchant = new Merchant
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            LegalName = request.LegalName,
            DisplayName = request.DisplayName,
            ContactPhone = request.ContactPhone,
            ContactEmail = request.ContactEmail,
            KycStatus = KycStatus.Pending,
            IsActive = true
        };
        _dbContext.Set<Merchant>().Add(merchant);

        var owner = new MerchantUser
        {
            Id = Guid.NewGuid(),
            MerchantId = merchant.Id,
            OrganizationId = organizationId,
            Email = ownerEmail,
            PasswordHash = _passwordHasher.Hash(request.OwnerPassword),
            FullName = request.OwnerFullName,
            Role = MerchantUserRole.Owner,
            IsActive = true
        };
        _dbContext.Set<MerchantUser>().Add(owner);

        var address = new MerchantAddress
        {
            Id = Guid.NewGuid(),
            MerchantId = merchant.Id,
            OrganizationId = organizationId,
            Label = "Pickup",
            ProvinceId = request.ProvinceId,
            DistrictId = request.DistrictId,
            LocalLevelId = request.LocalLevelId,
            WardNumber = request.WardNumber,
            AddressLine = request.AddressLine,
            IsDefaultPickup = true
        };
        _dbContext.Set<MerchantAddress>().Add(address);

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "merchant.created", "Merchant", merchant.Id.ToString()), cancellationToken);

        return ToResponse(merchant);
    }

    public async Task<MerchantResponse?> GetMerchantAsync(Guid merchantId, CancellationToken cancellationToken = default)
    {
        var merchant = await _dbContext.Set<Merchant>().FirstOrDefaultAsync(m => m.Id == merchantId, cancellationToken);
        return merchant is null ? null : ToResponse(merchant);
    }

    public async Task<List<MerchantResponse>> ListMerchantsAsync(CancellationToken cancellationToken = default)
    {
        var merchants = await _dbContext.Set<Merchant>().ToListAsync(cancellationToken);
        return merchants.Select(ToResponse).ToList();
    }

    private static MerchantResponse ToResponse(Merchant merchant) => new(
        merchant.Id, merchant.LegalName, merchant.DisplayName, merchant.ContactPhone, merchant.ContactEmail,
        merchant.KycStatus, merchant.IsActive);
}
