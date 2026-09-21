using Courier.Notifications.Domain;
using Xunit;

namespace Courier.Notifications.Tests;

public class NotificationLocalesTests
{
    [Theory]
    [InlineData("en", true)]
    [InlineData("ne", true)]
    [InlineData("fr", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSupported_ReportsSupportedLocalesOnly(string? locale, bool expected) =>
        Assert.Equal(expected, NotificationLocales.IsSupported(locale));

    [Theory]
    [InlineData("ne", "ne")]
    [InlineData("NE", "ne")]
    [InlineData("fr", "en")]
    [InlineData(null, "en")]
    public void Normalize_FallsBackToEnglishForMissingOrUnsupported(string? locale, string expected) =>
        Assert.Equal(expected, NotificationLocales.Normalize(locale));

    [Fact]
    public void NewTemplate_DefaultsToEnglish() =>
        Assert.Equal("en", new NotificationTemplate { EventType = "shipment.created", Body = "b" }.Locale);
}
