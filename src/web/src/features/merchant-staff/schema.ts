import { z } from "zod";

export const inviteMerchantStaffSchema = z.object({
  email: z.string().email("Enter a valid email address."),
  password: z.string().min(8, "Password must be at least 8 characters."),
  fullName: z.string().min(1, "Full name is required."),
});

export type InviteMerchantStaffFormValues = z.infer<typeof inviteMerchantStaffSchema>;
