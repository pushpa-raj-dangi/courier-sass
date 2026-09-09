import { z } from "zod";

export const platformLoginSchema = z.object({
  email: z.string().email("Enter a valid email address."),
  password: z.string().min(1, "Password is required."),
});

export type PlatformLoginFormValues = z.infer<typeof platformLoginSchema>;
