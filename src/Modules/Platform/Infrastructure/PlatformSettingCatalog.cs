using Courier.Platform.Domain;

namespace Courier.Platform.Infrastructure;

// The fixed set of platform-wide settings this feature manages. Seeded into
// PlatformSettingDefinitions via migration HasData (PlatformSettingDefinitionConfiguration) —
// not user-creatable through the API; a SuperAdmin edits VALUES, not this catalog.
public static class PlatformSettingCatalog
{
    public const string CategoryPaymentGateway = "PaymentGateway";
    public const string CategoryNotifications = "Notifications";
    public const string CategorySecurity = "Security";
    public const string CategoryBranding = "Branding";

    public const string ValueTypeString = "String";
    public const string ValueTypeInt = "Int";
    public const string ValueTypeBool = "Bool";

    public static readonly IReadOnlyList<PlatformSettingDefinition> Definitions = new List<PlatformSettingDefinition>
    {
        new() { Key = "Esewa.ProductCode", Category = CategoryPaymentGateway, ConfigPath = "Esewa:ProductCode", ValueType = ValueTypeString, IsSecret = false, RequiresConfirmation = false, Description = "eSewa merchant product code." },
        new() { Key = "Esewa.SecretKey", Category = CategoryPaymentGateway, ConfigPath = "Esewa:SecretKey", ValueType = ValueTypeString, IsSecret = true, RequiresConfirmation = false, Description = "eSewa HMAC signing secret." },
        new() { Key = "Esewa.FormUrl", Category = CategoryPaymentGateway, ConfigPath = "Esewa:FormUrl", ValueType = ValueTypeString, IsSecret = false, RequiresConfirmation = false, Description = "eSewa checkout form endpoint." },
        new() { Key = "Esewa.StatusCheckUrl", Category = CategoryPaymentGateway, ConfigPath = "Esewa:StatusCheckUrl", ValueType = ValueTypeString, IsSecret = false, RequiresConfirmation = false, Description = "eSewa server-to-server status check endpoint." },
        new() { Key = "Khalti.SecretKey", Category = CategoryPaymentGateway, ConfigPath = "Khalti:SecretKey", ValueType = ValueTypeString, IsSecret = true, RequiresConfirmation = false, Description = "Khalti live secret key." },
        new() { Key = "Khalti.InitiateUrl", Category = CategoryPaymentGateway, ConfigPath = "Khalti:InitiateUrl", ValueType = ValueTypeString, IsSecret = false, RequiresConfirmation = false, Description = "Khalti payment initiate endpoint." },
        new() { Key = "Khalti.LookupUrl", Category = CategoryPaymentGateway, ConfigPath = "Khalti:LookupUrl", ValueType = ValueTypeString, IsSecret = false, RequiresConfirmation = false, Description = "Khalti payment lookup endpoint." },
        new() { Key = "Khalti.WebsiteUrl", Category = CategoryPaymentGateway, ConfigPath = "Khalti:WebsiteUrl", ValueType = ValueTypeString, IsSecret = false, RequiresConfirmation = false, Description = "Website URL Khalti associates with this integration." },
        new() { Key = "Smtp.Host", Category = CategoryNotifications, ConfigPath = "Smtp:Host", ValueType = ValueTypeString, IsSecret = false, RequiresConfirmation = false, Description = "SMTP server host." },
        new() { Key = "Smtp.Port", Category = CategoryNotifications, ConfigPath = "Smtp:Port", ValueType = ValueTypeInt, IsSecret = false, RequiresConfirmation = false, Description = "SMTP server port." },
        new() { Key = "Smtp.From", Category = CategoryNotifications, ConfigPath = "Smtp:From", ValueType = ValueTypeString, IsSecret = false, RequiresConfirmation = false, Description = "From address for outgoing email." },
        new() { Key = "Jwt.SigningKey", Category = CategorySecurity, ConfigPath = "Jwt:SigningKey", ValueType = ValueTypeString, IsSecret = true, RequiresConfirmation = true, Description = "JWT signing key. Rotating this logs out every user platform-wide." },
        new() { Key = "Jwt.Issuer", Category = CategorySecurity, ConfigPath = "Jwt:Issuer", ValueType = ValueTypeString, IsSecret = false, RequiresConfirmation = false, Description = "JWT issuer claim." },
        new() { Key = "Jwt.Audience", Category = CategorySecurity, ConfigPath = "Jwt:Audience", ValueType = ValueTypeString, IsSecret = false, RequiresConfirmation = false, Description = "JWT audience claim." },
        new() { Key = "Jwt.AccessTokenMinutes", Category = CategorySecurity, ConfigPath = "Jwt:AccessTokenMinutes", ValueType = ValueTypeInt, IsSecret = false, RequiresConfirmation = false, Description = "Access token lifetime, in minutes." },
        new() { Key = "RateLimiting.ApiKeyRequestsPerMinute", Category = CategorySecurity, ConfigPath = "RateLimiting:ApiKeyRequestsPerMinute", ValueType = ValueTypeInt, IsSecret = false, RequiresConfirmation = false, Description = "Per-API-key request limit per minute." },
        new() { Key = "RateLimiting.PublicTrackingRequestsPerMinute", Category = CategorySecurity, ConfigPath = "RateLimiting:PublicTrackingRequestsPerMinute", ValueType = ValueTypeInt, IsSecret = false, RequiresConfirmation = false, Description = "Per-IP public tracking request limit per minute." },
        new() { Key = "Frontend.BaseUrl", Category = CategoryBranding, ConfigPath = "Frontend:BaseUrl", ValueType = ValueTypeString, IsSecret = false, RequiresConfirmation = false, Description = "Base URL used to build links in outgoing emails/notifications." },
        new() { Key = "Billing.GraceDays", Category = CategoryBranding, ConfigPath = "Billing:GraceDays", ValueType = ValueTypeInt, IsSecret = false, RequiresConfirmation = false, Description = "Days after an invoice's due date before an organization is suspended." },
    };
}
