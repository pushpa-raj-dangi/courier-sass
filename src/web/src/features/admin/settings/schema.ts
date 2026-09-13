import { z } from "zod";

export const organizationProfileSchema = z.object({
  supportEmail: z.string().email("Enter a valid email.").optional().or(z.literal("")),
  supportPhone: z.string().optional().or(z.literal("")),
  addressLine: z.string().optional().or(z.literal("")),
  logoUrl: z.string().optional().or(z.literal("")),
  timeZoneId: z.string().min(1, "Timezone is required."),
  businessHours: z.string().optional().or(z.literal("")),
});

export type OrganizationProfileFormValues = z.infer<typeof organizationProfileSchema>;

export const securityPolicySchema = z.object({
  refreshTokenLifetimeDays: z.coerce.number().int().min(1, "Must be at least 1 day.").max(30, "Must be at most 30 days."),
  minPasswordLength: z.coerce.number().int().min(8, "Must be at least 8 characters.").max(64, "Must be at most 64 characters."),
});

export type SecurityPolicyFormValues = z.infer<typeof securityPolicySchema>;
