import { z } from "zod";

export const notificationTemplateSchema = z.object({
  body: z.string().min(1, "Body is required."),
  isActive: z.boolean(),
});

export type NotificationTemplateFormValues = z.infer<typeof notificationTemplateSchema>;
