using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Merchants.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Merchants.Application;

public class MerchantStaffService
{
    private readonly DbContext _dbContext;
    private readonly IAuditLogger _auditLogger;
    private readonly IMerchantPasswordHasher _passwordHasher;

    public MerchantStaffService(DbContext dbContext, IAuditLogger auditLogger, IMerchantPasswordHasher passwordHasher)
    {
        _dbContext = dbContext;
        _auditLogger = auditLogger;
        _passwordHasher = passwordHasher;
    }

    public async Task<MerchantStaffResponse> InviteStaffAsync(InviteMerchantStaffRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var merchantId = actingUser.GetMerchantId()
            ?? throw new MerchantValidationException("Acting merchant could not be resolved.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new MerchantValidationException("Acting user could not be resolved.");

        var actingMerchantUser = await _dbContext.Set<MerchantUser>()
            .FirstOrDefaultAsync(mu => mu.Id == actingUserId, cancellationToken)
            ?? throw new MerchantValidationException("Acting merchant user could not be resolved.");

        var email = request.Email.ToLowerInvariant();
        var emailTaken = await _dbContext.Set<MerchantUser>().IgnoreQueryFilters()
            .AnyAsync(mu => mu.Email == email, cancellationToken);
        if (emailTaken)
        {
            throw new MerchantValidationException($"Email '{email}' is already registered to a merchant account.");
        }

        var staff = new MerchantUser
        {
            Id = Guid.NewGuid(),
            MerchantId = merchantId,
            OrganizationId = actingMerchantUser.OrganizationId,
            Email = email,
            PasswordHash = _passwordHasher.Hash(request.Password),
            FullName = request.FullName,
            Role = MerchantUserRole.Staff,
            IsActive = true
        };
        _dbContext.Set<MerchantUser>().Add(staff);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(actingMerchantUser.OrganizationId, actingUserId, "merchant_staff.invited", "MerchantUser", staff.Id.ToString()), cancellationToken);

        return ToResponse(staff);
    }

    public async Task<List<MerchantStaffResponse>> ListStaffAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var merchantId = user.GetMerchantId()
            ?? throw new MerchantValidationException("Acting merchant could not be resolved.");

        var staff = await _dbContext.Set<MerchantUser>()
            .Where(mu => mu.MerchantId == merchantId)
            .ToListAsync(cancellationToken);
        return staff.Select(ToResponse).ToList();
    }

    private static MerchantStaffResponse ToResponse(MerchantUser merchantUser) => new(
        merchantUser.Id, merchantUser.Email, merchantUser.FullName, merchantUser.Role, merchantUser.IsActive);
}
