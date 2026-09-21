import { z } from "zod";

export const loginSchema = z.object({
  email: z.string().email("emailInvalid"),
  password: z.string().min(1, "passwordRequired"),
});

export type LoginFormValues = z.infer<typeof loginSchema>;

// registerSchema messages are catalog keys under auth.validation; translated at display time.
export type RegisterErrorKey =
  | "organizationNameMin"
  | "slugMin"
  | "slugFormat"
  | "fullNameRequired"
  | "emailInvalid"
  | "passwordMin"
  | "confirmRequired"
  | "passwordsMismatch";

export const registerSchema = z
  .object({
    organizationName: z.string().min(2, "organizationNameMin"),
    organizationSlug: z
      .string()
      .min(2, "slugMin")
      .regex(/^[a-z0-9]+(-[a-z0-9]+)*$/, "slugFormat"),
    ownerFullName: z.string().min(2, "fullNameRequired"),
    ownerEmail: z.string().email("emailInvalid"),
    ownerPassword: z.string().min(8, "passwordMin"),
    confirmPassword: z.string().min(1, "confirmRequired"),
  })
  .refine((values) => values.ownerPassword === values.confirmPassword, {
    message: "passwordsMismatch",
    path: ["confirmPassword"],
  });

export type RegisterFormValues = z.infer<typeof registerSchema>;

export const acceptInviteSchema = z
  .object({
    password: z.string().min(8, "Password must be at least 8 characters."),
    confirmPassword: z.string().min(1, "Confirm your password."),
  })
  .refine((values) => values.password === values.confirmPassword, {
    message: "Passwords don't match.",
    path: ["confirmPassword"],
  });

export type AcceptInviteFormValues = z.infer<typeof acceptInviteSchema>;
