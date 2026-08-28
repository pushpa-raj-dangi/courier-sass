using Courier.Reports.Infrastructure;

namespace Courier.Reports.Tests;

public class CsvWriterTests
{
    [Fact]
    public void Write_EscapesCommaQuoteAndNewline()
    {
        var csv = CsvWriter.Write(
            new[] { "Name", "Note" },
            new[] { (IReadOnlyList<string?>)new[] { "Acme, Inc.", "Said \"hello\"\nagain" } });

        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Name,Note", lines[0]);
        Assert.Equal("\"Acme, Inc.\",\"Said \"\"hello\"\"\nagain\"", lines[1]);
    }

    [Fact]
    public void Write_NullField_RendersAsEmptyString()
    {
        var csv = CsvWriter.Write(new[] { "A" }, new[] { (IReadOnlyList<string?>)new string?[] { null } });
        // Deviation from brief: the row's only field is an empty string, so
        // StringSplitOptions.RemoveEmptyEntries would strip the very line under test
        // (leaving a 1-element array and an IndexOutOfRangeException on lines[1]).
        // Split without RemoveEmptyEntries so the empty row line survives.
        var lines = csv.Split("\r\n");
        Assert.Equal("", lines[1]);
    }

    [Fact]
    public void Write_PlainFields_NoQuoting()
    {
        var csv = CsvWriter.Write(new[] { "Awb" }, new[] { (IReadOnlyList<string?>)new[] { "AWB123456" } });
        Assert.Contains("AWB123456", csv);
        Assert.DoesNotContain("\"AWB123456\"", csv);
    }

    [Theory]
    [InlineData("=cmd|'/c calc'!A1")]
    [InlineData("+1+1")]
    [InlineData("-2+3")]
    [InlineData("@SUM(A1:A2)")]
    public void Write_FieldStartingWithFormulaCharacter_IsPrefixedWithSingleQuote(string maliciousField)
    {
        var csv = CsvWriter.Write(
            new[] { "MerchantName" },
            new[] { (IReadOnlyList<string?>)new[] { maliciousField } });

        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("'" + maliciousField, lines[1]);
    }
}
