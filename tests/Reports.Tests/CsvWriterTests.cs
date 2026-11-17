using Courier.SharedKernel;

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

    [Fact]
    public void WriteLines_MatchesWrite_ForSameContent()
    {
        var headers = new[] { "Name", "Note" };
        var rows = new[] { (IReadOnlyList<string?>)new[] { "Acme, Inc.", "Said \"hello\"\nagain" } };

        var expected = CsvWriter.Write(headers, rows);
        var actual = string.Concat(CsvWriter.WriteLines(headers, rows));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void WriteLines_YieldsOneLinePerRowPlusHeader()
    {
        var headers = new[] { "Awb" };
        var rows = new[]
        {
            (IReadOnlyList<string?>)new[] { "AWB1" },
            (IReadOnlyList<string?>)new[] { "AWB2" },
        };

        var lines = CsvWriter.WriteLines(headers, rows).ToList();

        Assert.Equal(3, lines.Count);
        Assert.Equal("Awb\r\n", lines[0]);
        Assert.Equal("AWB1\r\n", lines[1]);
        Assert.Equal("AWB2\r\n", lines[2]);
    }

    [Theory]
    [InlineData("-50.00")]
    [InlineData("-7")]
    [InlineData("2350.00")]
    public void WriteLines_PlainNumber_IsNotGuarded(string number)
    {
        var lines = CsvWriter.WriteLines(
            new[] { "Amount" },
            new[] { (IReadOnlyList<string?>)new[] { number } }).ToList();

        Assert.Equal(number + "\r\n", lines[1]);
    }

    [Fact]
    public void WriteLines_NegativeExpressionThatIsNotAPlainNumber_IsStillGuarded()
    {
        var lines = CsvWriter.WriteLines(
            new[] { "Amount" },
            new[] { (IReadOnlyList<string?>)new[] { "-1+1" } }).ToList();

        Assert.Equal("'-1+1\r\n", lines[1]);
    }

    [Theory]
    [InlineData("=cmd|'/c calc'!A1")]
    [InlineData("+1+1")]
    public void WriteLines_FieldStartingWithFormulaCharacter_IsPrefixedWithSingleQuote(string maliciousField)
    {
        var lines = CsvWriter.WriteLines(
            new[] { "MerchantName" },
            new[] { (IReadOnlyList<string?>)new[] { maliciousField } }).ToList();

        Assert.Equal("'" + maliciousField + "\r\n", lines[1]);
    }
}
