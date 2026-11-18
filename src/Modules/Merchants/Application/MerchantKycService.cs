using System.Text.Json;
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

    public async Task<MerchantPortalKycDocumentResponse> UploadDocumentAsync(
        KycDocumentType documentType, Stream content, string contentType, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(documentType))
        {
            throw new MerchantValidationException("Invalid document type.");
        }

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
            Status = KycStatus.Pending,
            UploadedAt = DateTime.UtcNow
        };
        _dbContext.Set<MerchantKycDocument>().Add(document);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(merchantUser.OrganizationId, actingUserId, "merchant_kyc.uploaded", "MerchantKycDocument", document.Id.ToString()), cancellationToken);

        return ToPortalResponse(document);
    }

    public async Task<List<MerchantPortalKycDocumentResponse>> ListDocumentsAsync(ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var merchantId = actingUser.GetMerchantId()
            ?? throw new MerchantValidationException("Acting merchant could not be resolved.");

        var documents = await _dbContext.Set<MerchantKycDocument>()
            .Where(d => d.MerchantId == merchantId)
            .ToListAsync(cancellationToken);
        return documents.Select(ToPortalResponse).ToList();
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

        if (!Enum.IsDefined(request.Status))
        {
            throw new MerchantValidationException("Invalid review status.");
        }

        string? rejectionReason = null;
        if (request.Status == KycStatus.Rejected)
        {
            rejectionReason = request.RejectionReason?.Trim();
            if (string.IsNullOrEmpty(rejectionReason))
            {
                throw new MerchantValidationException("A rejection reason is required.");
            }
            if (rejectionReason.Length > 500)
            {
                throw new MerchantValidationException("Rejection reason must be 500 characters or fewer.");
            }
        }

        var before = SerializeReviewState(document.Status, document.RejectionReason);

        document.Status = request.Status;
        document.RejectionReason = rejectionReason;
        document.ReviewedAt = DateTime.UtcNow;
        document.ReviewedByUserId = actingUserId;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(
            organizationId, actingUserId, "merchant_kyc.reviewed", "MerchantKycDocument", document.Id.ToString(),
            before, SerializeReviewState(document.Status, document.RejectionReason)), cancellationToken);

        return ToStaffResponse(document);
    }

    private static string SerializeReviewState(KycStatus status, string? rejectionReason) =>
        JsonSerializer.Serialize(new { status = status.ToString(), rejectionReason });

    private static MerchantKycDocumentResponse ToStaffResponse(MerchantKycDocument document) => new(
        document.Id, document.DocumentType, document.Status, document.ReviewedByUserId,
        document.UploadedAt, document.ReviewedAt, document.RejectionReason);

    private static MerchantPortalKycDocumentResponse ToPortalResponse(MerchantKycDocument document) => new(
        document.Id, document.DocumentType, document.Status,
        document.UploadedAt, document.ReviewedAt, document.RejectionReason);
}
