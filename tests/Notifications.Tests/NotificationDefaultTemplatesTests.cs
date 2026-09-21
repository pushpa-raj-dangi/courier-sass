using System.Text.RegularExpressions;
using Courier.Notifications.Application;
using Xunit;

namespace Courier.Notifications.Tests;

public class NotificationDefaultTemplatesTests
{
    private static readonly Regex Placeholder = new(@"\{[A-Za-z]+\}");

    [Fact]
    public void EnglishAndNepali_CoverTheSameEventTypes() =>
        Assert.Equal(
            NotificationDefaultTemplates.Email.Keys.OrderBy(k => k),
            NotificationDefaultTemplates.EmailNe.Keys.OrderBy(k => k));

    [Fact]
    public void EnglishAndNepali_UseTheSamePlaceholdersPerEvent()
    {
        foreach (var (eventType, english) in NotificationDefaultTemplates.Email)
        {
            var nepali = NotificationDefaultTemplates.EmailNe[eventType];
            Assert.Equal(
                Placeholder.Matches(english).Select(m => m.Value).OrderBy(v => v),
                Placeholder.Matches(nepali).Select(m => m.Value).OrderBy(v => v));
        }
    }

    [Fact]
    public void TryGetDefault_ReturnsLocaleSpecificBody()
    {
        Assert.True(NotificationDefaultTemplates.TryGetDefault("ne", "shipment.created", out var ne));
        Assert.True(NotificationDefaultTemplates.TryGetDefault("en", "shipment.created", out var en));
        Assert.NotEqual(en, ne);
        Assert.Contains("{Awb}", ne);
    }

    [Fact]
    public void TryGetDefault_UnknownLocaleOrEvent_ReturnsFalse()
    {
        Assert.False(NotificationDefaultTemplates.TryGetDefault("fr", "shipment.created", out _));
        Assert.False(NotificationDefaultTemplates.TryGetDefault("ne", "not.an.event", out _));
    }

    [Fact]
    public void IsKnownEvent_MatchesEnglishKeys()
    {
        Assert.True(NotificationDefaultTemplates.IsKnownEvent("user.invited"));
        Assert.False(NotificationDefaultTemplates.IsKnownEvent("nope"));
    }
}
