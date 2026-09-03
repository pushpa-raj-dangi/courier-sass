import { z } from "zod";

export const merchantLoginSchema = z.object({
  email: z.string().email("Enter a valid email address."),
  password: z.string().min(1, "Password is required."),
});

export type MerchantLoginFormValues = z.infer<typeof merchantLoginSchema>;
