using Courier.Notifications.Domain;

namespace Courier.Notifications.Application;

// Spec §2 fallback: org override (locale) -> compiled default (locale) -> org override (en) ->
// compiled default (en). A locale-appropriate compiled body deliberately beats an org's English
// override so a Nepali tenant never silently gets English when a Nepali default exists.
public static class NotificationTemplateResolver
{
    public static string? Resolve(string locale, string eventType, IReadOnlyDictionary<string, string> orgActiveBodiesByLocale)
    {
        if (orgActiveBodiesByLocale.TryGetValue(locale, out var orgLocaleBody))
        {
            return orgLocaleBody;
        }
        if (NotificationDefaultTemplates.TryGetDefault(locale, eventType, out var defaultLocaleBody))
        {
            return defaultLocaleBody;
        }
        if (orgActiveBodiesByLocale.TryGetValue(NotificationLocales.Default, out var orgEnglishBody))
        {
            return orgEnglishBody;
        }
        return NotificationDefaultTemplates.TryGetDefault(NotificationLocales.Default, eventType, out var englishDefault)
            ? englishDefault
            : null;
    }
}
