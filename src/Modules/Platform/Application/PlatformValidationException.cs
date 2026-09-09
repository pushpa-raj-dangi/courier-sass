namespace Courier.Platform.Application;

public class PlatformValidationException : Exception
{
    public PlatformValidationException(string message) : base(message) { }
}
