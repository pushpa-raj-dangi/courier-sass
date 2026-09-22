export type NotificationChannel = "Sms" | "Email" | "WhatsApp" | "Push";
export type NotificationLocale = "en" | "ne";

export interface NotificationTemplate {
  id: string;
  eventType: string;
  channel: NotificationChannel;
  locale: NotificationLocale;
  body: string;
  isActive: boolean;
}

export interface UpsertNotificationTemplateInput {
  eventType: string;
  channel: NotificationChannel;
  locale: NotificationLocale;
  body: string;
  isActive: boolean;
}
