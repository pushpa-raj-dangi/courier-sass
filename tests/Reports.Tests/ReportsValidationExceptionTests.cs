using Courier.Reports.Application;

namespace Courier.Reports.Tests;

public class ReportsValidationExceptionTests
{
    [Fact]
    public void ReportsValidationException_CarriesMessage()
    {
        var ex = new ReportsValidationException("Branch is outside your branch access for permission 'report.read'.");
        Assert.Equal("Branch is outside your branch access for permission 'report.read'.", ex.Message);
    }

    [Fact]
    public void InvalidDateRangeException_CarriesMessage()
    {
        var ex = new InvalidDateRangeException("'from' must not be after 'to'.");
        Assert.Equal("'from' must not be after 'to'.", ex.Message);
    }
}
