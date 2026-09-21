import { z } from "zod";
import { CALENDARS, LOCALES } from "@/i18n/config";

export const organizationProfileSchema = z.object({
  supportEmail: z.string().email("Enter a valid email.").optional().or(z.literal("")),
  supportPhone: z.string().optional().or(z.literal("")),
  addressLine: z.string().optional().or(z.literal("")),
  logoUrl: z.string().optional().or(z.literal("")),
  timeZoneId: z.string().min(1, "Timezone is required."),
  businessHours: z.string().optional().or(z.literal("")),
  defaultLocale: z.enum(LOCALES),
  calendarPreference: z.enum(CALENDARS),
});

export type OrganizationProfileFormValues = z.infer<typeof organizationProfileSchema>;

export const securityPolicySchema = z.object({
  // Don't use z.coerce.number() here - its *input* type is `unknown`, which conflicts with
  // useForm<SecurityPolicyFormValues>'s output-inferred `number`, since zodResolver's Resolver
  // type is checked against both the schema's input and output shapes (see the branches schema's
  // wardNumber comment for the full explanation). SecurityPolicyForm instead passes
  // `valueAsNumber: true` to register() so RHF converts the DOM input's string value before it
  // reaches Zod, keeping both the input and output types `number`.
  refreshTokenLifetimeDays: z.number().int().min(1, "Must be at least 1 day.").max(30, "Must be at most 30 days."),
  minPasswordLength: z.number().int().min(8, "Must be at least 8 characters.").max(64, "Must be at most 64 characters."),
});

export type SecurityPolicyFormValues = z.infer<typeof securityPolicySchema>;
