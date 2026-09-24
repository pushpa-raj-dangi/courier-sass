using System.Buffers.Text;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Courier.Audit.Application;
using Courier.International.Application.Carriers;
using Courier.International.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.International.Application;

public class CarrierAccountService
{
    private const int MaxDisplayNameLength = 200;
    private const int MaxCredentialEntries = 20;
    private const int MaxCredentialKeyLength = 100;
    private const int MaxCredentialValueLength = 2000;

    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly ICarrierProviderRegistry _registry;
    private readonly ICarrierSecretProtector _secretProtector;

    public CarrierAccountService(DbContext dbContext, ICurrentTenantProvider currentTenantProvider, IAuditLogger auditLogger,
        ICarrierProviderRegistry registry, ICarrierSecretProtector secretProtector)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _registry = registry;
        _secretProtector = secretProtector;
    }

    public async Task<IReadOnlyList<CarrierAccountResponse>> ListAsync(CancellationToken cancellationToken = default) =>
        (await _dbContext.Set<CarrierAccount>().OrderBy(a => a.DisplayName).ToListAsync(cancellationToken)).Select(ToResponse).ToList();

    public async Task<CarrierAccountResponse?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var account = await _dbContext.Set<CarrierAccount>().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        return account is null ? null : ToResponse(account);
    }

    public async Task<CarrierAccountSecretResponse> CreateAsync(CreateCarrierAccountRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CarrierAccountValidationException("No active organization context.");

        var provider = _registry.Find(request.CarrierCode?.Trim() ?? string.Empty)
            ?? throw new CarrierAccountValidationException($"Unknown carrier code '{request.CarrierCode}'. Registered: {string.Join(", ", _registry.RegisteredCodes)}.");
        var displayName = ValidateDisplayName(request.DisplayName);
        ValidateCapabilities(provider, request.PollingEnabled, request.GenerateWebhookSecret);
        var credentials = request.Credentials is null ? null : ValidateCredentials(request.Credentials);

        string? webhookSecret = request.GenerateWebhookSecret ? NewSecret() : null;
        var now = DateTimeOffset.UtcNow;
        var account = new CarrierAccount
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            CarrierCode = provider.CarrierCode,
            DisplayName = displayName,
            IsActive = true,
            PollingEnabled = request.PollingEnabled,
            EncryptedCredentials = credentials is null ? null : _secretProtector.ProtectCredentials(credentials),
            EncryptedWebhookSecret = webhookSecret is null ? null : _secretProtector.ProtectWebhookSecret(webhookSecret),
            WebhookPublicId = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(24)),
            CreatedAt = now,
            UpdatedAt = now
        };
        _dbContext.Set<CarrierAccount>().Add(account);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = ToResponse(account);
        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "carrier_account.created", "CarrierAccount", account.Id.ToString(),
            After: JsonSerializer.Serialize(response)), cancellationToken);
        return new CarrierAccountSecretResponse(response, webhookSecret);
    }

    public async Task<CarrierAccountResponse?> UpdateAsync(Guid id, UpdateCarrierAccountRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var account = await _dbContext.Set<CarrierAccount>().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (account is null)
        {
            return null;
        }
        var provider = _registry.Find(account.CarrierCode)
            ?? throw new CarrierAccountValidationException($"Carrier '{account.CarrierCode}' is no longer registered.");
        var displayName = ValidateDisplayName(request.DisplayName);
        ValidateCapabilities(provider, request.PollingEnabled, generateWebhookSecret: false);

        var before = JsonSerializer.Serialize(ToResponse(account));
        account.DisplayName = displayName;
        account.IsActive = request.IsActive;
        account.PollingEnabled = request.PollingEnabled;
        account.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = ToResponse(account);
        await _auditLogger.LogAsync(new AuditEntry(account.OrganizationId, actingUser.GetUserId(), "carrier_account.updated", "CarrierAccount", account.Id.ToString(),
            Before: before, After: JsonSerializer.Serialize(response)), cancellationToken);
        return response;
    }

    public async Task<CarrierAccountResponse?> ReplaceCredentialsAsync(Guid id, ReplaceCarrierCredentialsRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var account = await _dbContext.Set<CarrierAccount>().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (account is null)
        {
            return null;
        }
        var credentials = ValidateCredentials(request.Credentials ?? new Dictionary<string, string>());
        account.EncryptedCredentials = credentials.Values.Count == 0 ? null : _secretProtector.ProtectCredentials(credentials);
        account.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Audited without values — only which keys were set.
        await _auditLogger.LogAsync(new AuditEntry(account.OrganizationId, actingUser.GetUserId(), "carrier_account.credentials_replaced", "CarrierAccount", account.Id.ToString(),
            After: JsonSerializer.Serialize(new { keys = credentials.Values.Keys.OrderBy(k => k).ToList() })), cancellationToken);
        return ToResponse(account);
    }

    public async Task<CarrierAccountSecretResponse?> RotateWebhookSecretAsync(Guid id, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var account = await _dbContext.Set<CarrierAccount>().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (account is null)
        {
            return null;
        }
        var provider = _registry.Find(account.CarrierCode)
            ?? throw new CarrierAccountValidationException($"Carrier '{account.CarrierCode}' is no longer registered.");
        if (!provider.Capabilities.SupportsWebhooks)
        {
            throw new CarrierAccountValidationException($"Carrier '{account.CarrierCode}' does not support webhooks.");
        }

        var secret = NewSecret();
        account.EncryptedWebhookSecret = _secretProtector.ProtectWebhookSecret(secret);
        account.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(account.OrganizationId, actingUser.GetUserId(), "carrier_account.webhook_secret_rotated", "CarrierAccount", account.Id.ToString()), cancellationToken);
        return new CarrierAccountSecretResponse(ToResponse(account), secret);
    }

    public static CarrierAccountResponse ToResponse(CarrierAccount account) => new(
        account.Id, account.CarrierCode, account.DisplayName, account.IsActive, account.PollingEnabled,
        account.EncryptedCredentials is not null, account.EncryptedWebhookSecret is not null,
        $"/api/v1/carrier-webhooks/{account.WebhookPublicId}", account.CreatedAt, account.UpdatedAt);

    private static string NewSecret() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    private static string ValidateDisplayName(string? displayName)
    {
        var trimmed = displayName?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > MaxDisplayNameLength)
        {
            throw new CarrierAccountValidationException($"Display name is required and must be at most {MaxDisplayNameLength} characters.");
        }
        return trimmed;
    }

    private static void ValidateCapabilities(ICarrierProvider provider, bool pollingEnabled, bool generateWebhookSecret)
    {
        if (pollingEnabled && !provider.Capabilities.SupportsPolling)
        {
            throw new CarrierAccountValidationException($"Carrier '{provider.CarrierCode}' does not support polling.");
        }
        if (generateWebhookSecret && !provider.Capabilities.SupportsWebhooks)
        {
            throw new CarrierAccountValidationException($"Carrier '{provider.CarrierCode}' does not support webhooks.");
        }
    }

    private static CarrierCredentials ValidateCredentials(Dictionary<string, string> credentials)
    {
        if (credentials.Count > MaxCredentialEntries)
        {
            throw new CarrierAccountValidationException($"At most {MaxCredentialEntries} credential entries are allowed.");
        }
        foreach (var (key, value) in credentials)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > MaxCredentialKeyLength)
            {
                throw new CarrierAccountValidationException($"Credential names are required and must be at most {MaxCredentialKeyLength} characters.");
            }
            if (string.IsNullOrWhiteSpace(value) || value.Length > MaxCredentialValueLength)
            {
                throw new CarrierAccountValidationException($"Credential '{key}' must have a value of at most {MaxCredentialValueLength} characters.");
            }
        }
        return new CarrierCredentials(new Dictionary<string, string>(credentials));
    }
}
