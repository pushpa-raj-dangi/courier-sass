using Courier.Notifications.Application;
using Xunit;

namespace Courier.Notifications.Tests;

public class NotificationTemplateResolverTests
{
    private static readonly IReadOnlyDictionary<string, string> None = new Dictionary<string, string>();
    private const string Evt = "shipment.created";

    private static string Default(string locale)
    {
        NotificationDefaultTemplates.TryGetDefault(locale, Evt, out var body);
        return body;
    }

    [Fact]
    public void OrgOverrideForResolvedLocale_Wins() =>
        Assert.Equal("ne-override", NotificationTemplateResolver.Resolve("ne", Evt,
            new Dictionary<string, string> { ["ne"] = "ne-override", ["en"] = "en-override" }));

    [Fact]
    public void NoOrgOverride_UsesCompiledDefaultForLocale() =>
        Assert.Equal(Default("ne"), NotificationTemplateResolver.Resolve("ne", Evt, None));

    [Fact]
    public void CompiledLocaleDefault_BeatsOrgEnglishOverride() =>
        Assert.Equal(Default("ne"), NotificationTemplateResolver.Resolve("ne", Evt,
            new Dictionary<string, string> { ["en"] = "en-override" }));

    [Fact]
    public void UnsupportedLocale_FallsToOrgEnglishOverride() =>
        Assert.Equal("en-override", NotificationTemplateResolver.Resolve("fr", Evt,
            new Dictionary<string, string> { ["en"] = "en-override" }));

    [Fact]
    public void UnsupportedLocaleNoOverride_FallsToCompiledEnglish() =>
        Assert.Equal(Default("en"), NotificationTemplateResolver.Resolve("fr", Evt, None));

    [Fact]
    public void English_OrgOverrideBeatsCompiled() =>
        Assert.Equal("en-override", NotificationTemplateResolver.Resolve("en", Evt,
            new Dictionary<string, string> { ["en"] = "en-override" }));

    [Fact]
    public void UnknownEvent_ReturnsNull() =>
        Assert.Null(NotificationTemplateResolver.Resolve("en", "not.an.event", None));
}
