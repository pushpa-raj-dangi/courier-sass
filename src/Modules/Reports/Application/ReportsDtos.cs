namespace Courier.Reports.Application;

public sealed class ReportsValidationException : Exception
{
    public ReportsValidationException(string message) : base(message)
    {
    }
}

public sealed class InvalidDateRangeException : Exception
{
    public InvalidDateRangeException(string message) : base(message)
    {
    }
}
