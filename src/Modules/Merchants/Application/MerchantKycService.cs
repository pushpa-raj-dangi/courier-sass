using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Merchants.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Merchants.Application;

public class MerchantKycService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly IObjectStorage _objectStorage;

    public MerchantKycService(DbContext dbContext, ICurrentTenantProvider currentTenantProvider, IAuditLogger auditLogger, IObjectStorage objectStorage)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _objectStorage = objectStorage;
    }

    public async Task<MerchantKycDocumentResponse> UploadDocumentAsync(
        KycDocumentType documentType, Stream content, string contentType, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var merchantId = actingUser.GetMerchantId()
            ?? throw new MerchantValidationException("Acting merchant could not be resolved.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new MerchantValidationException("Acting user could not be resolved.");

        var merchantUser = await _dbContext.Set<MerchantUser>().FirstAsync(mu => mu.Id == actingUserId, cancellationToken);

        var storageKey = await _objectStorage.UploadAsync($"kyc/{merchantId}", content, contentType, cancellationToken);

        var document = new MerchantKycDocument
        {
            Id = Guid.NewGuid(),
            MerchantId = merchantId,
            OrganizationId = merchantUser.OrganizationId,
            DocumentType = documentType,
            StorageKey = storageKey,
            Status = KycStatus.Pending
        };
        _dbContext.Set<MerchantKycDocument>().Add(document);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(merchantUser.OrganizationId, actingUserId, "merchant_kyc.uploaded", "MerchantKycDocument", document.Id.ToString()), cancellationToken);

        return ToResponse(document);
    }

    public async Task<List<MerchantKycDocumentResponse>> ListDocumentsAsync(Guid merchantId, CancellationToken cancellationToken = default)
    {
        var documents = await _dbContext.Set<MerchantKycDocument>()
            .Where(d => d.MerchantId == merchantId)
            .ToListAsync(cancellationToken);
        return documents.Select(ToResponse).ToList();
    }

    public async Task<MerchantKycDocumentResponse?> ReviewDocumentAsync(
        Guid merchantId, Guid documentId, ReviewKycDocumentRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new MerchantValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new MerchantValidationException("Acting user could not be resolved.");

        // Filtered directly against MerchantKycDocument.OrganizationId (denormalized from
        // Merchant.OrganizationId at upload time, see MerchantKycDocument.cs) rather than a
        // separate "look up Merchant first" step — a merchantId/documentId pair belonging to a
        // different org simply won't be found here.
        var document = await _dbContext.Set<MerchantKycDocument>()
            .FirstOrDefaultAsync(d => d.Id == documentId && d.MerchantId == merchantId && d.OrganizationId == organizationId, cancellationToken);
        if (document is null)
        {
            return null;
        }

        document.Status = request.Status;
        document.ReviewedByUserId = actingUserId;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "merchant_kyc.reviewed", "MerchantKycDocument", document.Id.ToString()), cancellationToken);

        return ToResponse(document);
    }

    private static MerchantKycDocumentResponse ToResponse(MerchantKycDocument document) => new(
        document.Id, document.DocumentType, document.Status, document.ReviewedByUserId);
}
