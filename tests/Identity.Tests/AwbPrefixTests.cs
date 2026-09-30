using Courier.Identity.Domain;
using Xunit;

namespace Courier.Identity.Tests;

public class AwbPrefixTests
{
    [Theory]
    [InlineData("ktm-express", "KTMEXP")]
    [InlineData("ab", "AB")]
    [InlineData("Pokhara Cargo 1", "POKHAR")]
    [InlineData("a1-b2", "A1B2")]
    public void Derive_UppercasesStripsAndTruncatesTheSlug(string slug, string expected)
    {
        Assert.Equal(expected, AwbPrefix.Derive(slug, Guid.NewGuid()));
    }

    [Fact]
    public void Derive_SymbolOnlySlug_FallsBackToTheOrganizationId()
    {
        var organizationId = Guid.Parse("0a1b2c3d-0000-0000-0000-000000000000");

        Assert.Equal("0A1B2C", AwbPrefix.Derive("!!!", organizationId));
    }

    [Theory]
    [InlineData("INTLOR", 1, "INTLOR")]
    [InlineData("INTLOR", 2, "INTLO2")]
    [InlineData("INTLOR", 10, "INTL10")]
    [InlineData("AB", 2, "AB2")]
    [InlineData("AB", 123, "AB123")]
    public void Candidate_KeepsTheBaseFirstThenReplacesItsTailWithTheAttemptNumber(string basePrefix, int attempt, string expected)
    {
        Assert.Equal(expected, AwbPrefix.Candidate(basePrefix, attempt));
    }

    [Fact]
    public void Candidate_NeverExceedsTheMaximumLength()
    {
        Assert.True(AwbPrefix.Candidate("ABCDEF", 99999).Length <= AwbPrefix.MaxLength);
    }
}
