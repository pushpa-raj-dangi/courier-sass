using Courier.Audit.Application;
using Courier.Platform.Domain;
using Courier.Platform.Infrastructure;
using Courier.Platform.Infrastructure.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

namespace Courier.Platform.Application;

public record PlatformSettingResponse(
    string Key,
    string Category,
    string ValueType,
    bool IsSecret,
    bool RequiresConfirmation,
    string? Description,
    bool IsOverridden,
    string? DisplayValue,
    DateTime? UpdatedAt,
    Guid? UpdatedByUserId);

public class PlatformSettingsValidationException : Exception
{
    public PlatformSettingsValidationException(string message) : base(message) { }
}

public class PlatformSettingsService
{
    private const string ConfirmationPhrase = "REVOKE ALL SESSIONS";
    private const string ProtectorPurpose = "PlatformSettings.Secrets";

    private readonly DbContext _dbContext;
    private readonly IAuditLogger _auditLogger;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly IConnectionMultiplexer _redis;
    private readonly IConfiguration _configuration;
    private readonly IPlatformSettingsCache _cache;

    public PlatformSettingsService(
        DbContext dbContext,
        IAuditLogger auditLogger,
        IDataProtectionProvider dataProtectionProvider,
        IConnectionMultiplexer redis,
        IConfiguration configuration,
        IPlatformSettingsCache cache)
    {
        _dbContext = dbContext;
        _auditLogger = auditLogger;
        _dataProtectionProvider = dataProtectionProvider;
        _redis = redis;
        _configuration = configuration;
        _cache = cache;
    }

    public async Task<List<PlatformSettingResponse>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var definitions = await _dbContext.Set<PlatformSettingDefinition>()
            .OrderBy(d => d.Category).ThenBy(d => d.Key)
            .ToListAsync(cancellationToken);
        var settings = await _dbContext.Set<PlatformSetting>().ToDictionaryAsync(s => s.Key, cancellationToken);
        var protector = _dataProtectionProvider.CreateProtector(ProtectorPurpose);

        return definitions.Select(d =>
        {
            settings.TryGetValue(d.Key, out var setting);
            var isOverridden = setting?.IsOverridden ?? false;
            string? displayValue = null;
            if (isOverridden && setting!.Value is not null)
            {
                displayValue = d.IsSecret ? MaskSecret(protector.Unprotect(setting.Value)) : setting.Value;
            }
            return new PlatformSettingResponse(
                d.Key, d.Category, d.ValueType, d.IsSecret, d.RequiresConfirmation, d.Description,
                isOverridden, displayValue, setting?.UpdatedAt, setting?.UpdatedByUserId);
        }).ToList();
    }

    public async Task UpdateAsync(string key, string value, string? confirmationPhrase, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        var definition = await _dbContext.Set<PlatformSettingDefinition>().FirstOrDefaultAsync(d => d.Key == key, cancellationToken)
            ?? throw new PlatformSettingsValidationException($"Unknown setting key '{key}'.");

        ValidateType(definition, value);

        if (definition.RequiresConfirmation && confirmationPhrase != ConfirmationPhrase)
        {
            throw new PlatformSettingsValidationException($"This change requires typing the confirmation phrase \"{ConfirmationPhrase}\".");
        }

        var setting = await _dbContext.Set<PlatformSetting>().FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        var previousValue = setting?.IsOverridden == true ? setting.Value : null;

        var storedValue = definition.IsSecret
            ? _dataProtectionProvider.CreateProtector(ProtectorPurpose).Protect(value)
            : value;

        var now = DateTime.UtcNow;
        if (setting is null)
        {
            setting = new PlatformSetting { Key = key };
            _dbContext.Set<PlatformSetting>().Add(setting);
        }
        setting.Value = storedValue;
        setting.IsOverridden = true;
        setting.UpdatedAt = now;
        setting.UpdatedByUserId = actingUserId;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(
            null, actingUserId, "platform_setting.updated", "PlatformSetting", key,
            Before: definition.IsSecret ? "[REDACTED]" : previousValue,
            After: definition.IsSecret ? "[REDACTED]" : value,
            IsCrossTenantAction: true), cancellationToken);

        await PlatformSettingsReloader.ReloadAsync(_configuration, _cache, cancellationToken);
        await PublishChangeAsync();
    }

    public async Task ResetAsync(string key, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        var setting = await _dbContext.Set<PlatformSetting>().FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (setting is null || !setting.IsOverridden)
        {
            return;
        }

        var definition = await _dbContext.Set<PlatformSettingDefinition>().FirstOrDefaultAsync(d => d.Key == key, cancellationToken)
            ?? throw new PlatformSettingsValidationException($"Unknown setting key '{key}'.");
        var previousValue = setting.Value;

        setting.IsOverridden = false;
        setting.Value = null;
        setting.UpdatedAt = DateTime.UtcNow;
        setting.UpdatedByUserId = actingUserId;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(
            null, actingUserId, "platform_setting.reset", "PlatformSetting", key,
            Before: definition.IsSecret ? "[REDACTED]" : previousValue,
            After: null,
            IsCrossTenantAction: true), cancellationToken);

        await PlatformSettingsReloader.ReloadAsync(_configuration, _cache, cancellationToken);
        await PublishChangeAsync();
    }

    private async Task PublishChangeAsync()
    {
        await _redis.GetSubscriber().PublishAsync(RedisChannel.Literal(PlatformSettingsChangeListener.ChannelName), "reload");
    }

    private static string MaskSecret(string plaintext) => plaintext.Length <= 4 ? "••••" : $"••••{plaintext[^4..]}";

    private static void ValidateType(PlatformSettingDefinition definition, string value)
    {
        if (definition.ValueType == PlatformSettingCatalog.ValueTypeInt && !int.TryParse(value, out _))
        {
            throw new PlatformSettingsValidationException($"'{definition.Key}' must be a whole number.");
        }
        if (definition.ValueType == PlatformSettingCatalog.ValueTypeBool && !bool.TryParse(value, out _))
        {
            throw new PlatformSettingsValidationException($"'{definition.Key}' must be true or false.");
        }
    }
}
