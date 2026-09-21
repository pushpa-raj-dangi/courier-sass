namespace Courier.Identity.Application;

public sealed record OrganizationProfileDto(string? SupportEmail, string? SupportPhone, string? AddressLine, string? LogoUrl, string TimeZoneId, string? BusinessHours, string? DefaultLocale = null, string? CalendarPreference = null);
public sealed record SecurityPolicyDto(int RefreshTokenLifetimeDays, int MinPasswordLength);
public sealed record OperationalDefaultsDto(Guid? DefaultServiceTypeId, Guid? DefaultOriginBranchId);
public sealed record OrganizationSettingsDto(OrganizationProfileDto Profile, SecurityPolicyDto Security, OperationalDefaultsDto Defaults);

public sealed record OrganizationLocaleDto(string DefaultLocale, string CalendarPreference, string TimeZoneId);

public class OrganizationSettingsValidationException : Exception
{
    public OrganizationSettingsValidationException(string message) : base(message) { }
}
