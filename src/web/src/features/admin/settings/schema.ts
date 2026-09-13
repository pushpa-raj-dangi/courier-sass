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
