export type NotificationChannel = "Sms" | "Email" | "WhatsApp" | "Push";

export interface NotificationTemplate {
  id: string;
  eventType: string;
  channel: NotificationChannel;
  body: string;
  isActive: boolean;
}

export interface UpsertNotificationTemplateInput {
  eventType: string;
  channel: NotificationChannel;
  body: string;
  isActive: boolean;
}
