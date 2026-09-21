using Courier.Notifications.Domain;

namespace Courier.Notifications.Application;

public sealed record NotificationTemplateDto(Guid Id, string EventType, NotificationChannel Channel, string Body, bool IsActive, string Locale = NotificationLocales.Default);
