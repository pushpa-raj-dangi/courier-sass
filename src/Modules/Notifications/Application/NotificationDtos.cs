namespace Courier.Notifications.Application;

public class NotificationValidationException : Exception
{
    public NotificationValidationException(string message) : base(message) { }
}
